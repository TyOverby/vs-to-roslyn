function getQueryParams(query) {
    if (!query) {
        return {};
    }

    return (/^[?#]/.test(query) ? query.slice(1) : query)
        .split('&')
        .reduce((params, param) => {
            let [key, value] = param.split('=');
            params[key] = value ? decodeURIComponent(value.replace(/\+/g, ' ')) : '';
            return params;
        }, {});
}

var branch = getQueryParams(window.location.search).branch;
document.querySelector("#branchname").textContent = branch;

fetch(`api/listbuilds/${branch}`)
    .then(value => value.json())
    .then(async value => {
        value = value.splice(value.length - 10);
        for (let build of value) {
            await run("roslyn", branch, build);
        }
    })
    .catch(e => alert(e));
