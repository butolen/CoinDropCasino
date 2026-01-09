using System.Security.Cryptography;
using CoinDrop;
using Domain;
using Microsoft.EntityFrameworkCore;

public sealed class SessionCodeService
{
    private readonly IRepository<ApplicationUser> _userRepository;

    public SessionCodeService(IRepository<ApplicationUser> userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<int> GenerateAndStoreUniqueCodeAsync(int userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetAsync(userId, ct);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        const int min = 1000;
        const int maxExclusive = 10000;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            int code = RandomNumberGenerator.GetInt32(min, maxExclusive);

            bool exists = await _userRepository.Query()
                .AsNoTracking()
                .AnyAsync(u => u.SessionCode == code, ct);

            if (exists) continue;

            user.SessionCode = code;
            await _userRepository.UpdateAsync(user, ct); // enthält SaveChangesAsync
            return code;
        }

        throw new InvalidOperationException("Konnte keinen eindeutigen Session-Code erzeugen.");
    }

    public async Task<int?> GetCurrentCodeAsync(int userId, CancellationToken ct = default)
    {
        var user = await _userRepository.Query()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SessionCode)
            .FirstOrDefaultAsync(ct);

        return user;
    }

    public async Task ClearAsync(int userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetAsync(userId, ct);
        if (user is null) return;

        user.SessionCode = null;
        await _userRepository.UpdateAsync(user, ct);
    }
}