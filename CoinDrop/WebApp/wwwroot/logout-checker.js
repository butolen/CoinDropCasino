let logoutCheckInterval = null;
let isInitialized = false;

// Initialize polling
window.initializeLogoutPolling = () => {
    if (isInitialized) return;

    console.log('Initializing logout polling...');

    // Start polling after Blazor is fully loaded
    setTimeout(() => {
        startLogoutPolling();
    }, 2000); // 2 seconds delay to ensure Blazor is ready

    isInitialized = true;
};

// Start logout polling
async function startLogoutPolling() {
    try {
        console.log('Checking login status...');

        // Check if user is logged in
        const statusResponse = await fetch('/auth/check-login-status', {
            credentials: 'include',
            signal: AbortSignal.timeout(5000) // 5 second timeout
        });

        if (!statusResponse.ok) {
            console.log('Not logged in or check failed');
            return;
        }

        const statusData = await statusResponse.json();
        if (!statusData.isLoggedIn) {
            console.log('User not logged in');
            return;
        }

        console.log('User logged in, starting polling...');

        // Clear any existing interval
        if (logoutCheckInterval) {
            clearInterval(logoutCheckInterval);
            logoutCheckInterval = null;
        }

        // Set client token
        try {
            await fetch('/auth/set-client-token', {
                credentials: 'include',
                signal: AbortSignal.timeout(3000)
            });
            console.log('Client token set');
        } catch (tokenError) {
            console.warn('Could not set client token:', tokenError);
        }

        // Start polling every 15 seconds
        logoutCheckInterval = setInterval(() => {
            checkForForceLogout().catch(error => {
                console.error('Polling error:', error);
            });
        }, 15000);

        // Immediate first check
        await checkForForceLogout();

    } catch (error) {
        console.error('Error starting logout polling:', error);
        if (error.name !== 'AbortError') {
            // Retry after 10 seconds on non-abort errors
            setTimeout(startLogoutPolling, 10000);
        }
    }
}

// Stop logout polling
window.stopLogoutPolling = () => {
    if (logoutCheckInterval) {
        clearInterval(logoutCheckInterval);
        logoutCheckInterval = null;
    }
    console.log('Logout polling stopped');
};

// Check for force logout
async function checkForForceLogout() {
    try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 5000);

        const response = await fetch('/auth/check-logout', {
            credentials: 'include',
            signal: controller.signal,
            headers: {
                'Cache-Control': 'no-cache',
                'Pragma': 'no-cache'
            }
        });

        clearTimeout(timeoutId);

        if (!response.ok) {
            console.warn('Logout check failed:', response.status);
            return;
        }

        const data = await response.json();

        if (data.needsLogout) {
            console.log('Force logout required');
            await performForceLogout(data.message);
        }

    } catch (error) {
        if (error.name !== 'AbortError') {
            console.error('Error checking logout status:', error);
        }
    }
}

// Perform force logout
async function performForceLogout(message) {
    try {
        // Stop polling first
        window.stopLogoutPolling();

        // Show notification if not on login page
        if (message && !window.location.pathname.includes('/login')) {
            // Use a better notification method
            showNotification(message);
        }

        // Call server logout
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 5000);

        await fetch('/auth/force-logout', {
            method: 'POST',
            credentials: 'include',
            signal: controller.signal
        });

        clearTimeout(timeoutId);

        // Clear all cookies more reliably
        clearAllCookies();

        // Clear storage
        localStorage.clear();
        sessionStorage.clear();

        // Redirect to login
        setTimeout(() => {
            window.location.href = '/login?msg=force-logout';
        }, 100);

    } catch (error) {
        console.error('Error during force logout:', error);
        // Fallback redirect
        window.location.href = '/login?msg=force-logout';
    }
}

// Helper function to show notification
function showNotification(message) {
    // Check if we're in Blazor context
    if (window.Blazor) {
        // Create a simple alert or use a toast notification
        const notification = document.createElement('div');
        notification.style.cssText = `
            position: fixed;
            top: 20px;
            right: 20px;
            background: #ff4757;
            color: white;
            padding: 15px 20px;
            border-radius: 5px;
            z-index: 9999;
            box-shadow: 0 2px 10px rgba(0,0,0,0.2);
            max-width: 400px;
        `;
        notification.textContent = message;
        document.body.appendChild(notification);

        setTimeout(() => {
            if (document.body.contains(notification)) {
                document.body.removeChild(notification);
            }
        }, 5000);
    } else {
        // Simple alert as fallback
        alert(message);
    }
}

// Helper function to clear all cookies
function clearAllCookies() {
    const cookies = document.cookie.split(';');
    const hostname = window.location.hostname;
    const path = window.location.pathname;

    cookies.forEach(cookie => {
        const eqPos = cookie.indexOf('=');
        const name = eqPos > -1 ? cookie.substr(0, eqPos).trim() : cookie.trim();

        // Delete with domain and path
        document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=${path};`;
        document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/;`;
        document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=${path}; domain=${hostname};`;
        document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/; domain=${hostname};`;
    });
}

// Manual logout function
window.forceLogout = performForceLogout;

// Initialize when script loads
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', window.initializeLogoutPolling);
} else {
    window.initializeLogoutPolling();
}