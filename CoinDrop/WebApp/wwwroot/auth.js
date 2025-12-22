window.postRedirect = function (url, params) {
    const form = document.createElement("form");
    form.method = "POST";
    form.action = url;

    for (const key in params) {
        const input = document.createElement("input");
        input.type = "hidden";
        input.name = key;
        input.value = params[key];
        form.appendChild(input);
    }

    document.body.appendChild(form);
    form.submit();
}