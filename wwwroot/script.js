
function process(form) {
    let global_output = document.querySelector("#output");
    let branch = form.querySelector("#branch").value;
    let build = form.querySelector("#build").value;

    let container = document.createElement("div");
    container.innerHTML = `<h1>${branch} ${build}</h1>loading.gif`;

    global_output.appendChild(container);

    fetch(`/api/${branch}/${build}`)
        .then(value => value.json())
        .then(value => {
            let outputElement = null;

            if (value.length == 0) {
                outputElement = document.createTextNode("No elements!");
            } else {
                let table = document.createElement("table");
                let tr = document.createElement("tr");
                {
                    let td = document.createElement("td");
                    td.innerText = "VsoBuildTag";
                    tr.appendChild(td);
                }
                {
                    let td = document.createElement("td");
                    td.innerHTML= "RoslynBuildTag";
                    tr.appendChild(td);
                }
                {
                    let td = document.createElement("td");
                    td.innerText = "RoslynSha";
                    tr.appendChild(td);
                }
                table.appendChild(tr);
                for (let row of value) {
                    let tr = document.createElement("tr");
                    {
                        let td = document.createElement("td");
                        td.innerText = row.VsoBuildTag;
                        tr.appendChild(td);
                    }
                    {
                        let td = document.createElement("td");
                        td.innerText = row.RoslynBuildTag;
                        tr.appendChild(td);
                    }
                    {
                        let td = document.createElement("td");
                        td.innerHTML = `<a href="https://github.com/dotnet/roslyn/commit/${row.RoslynSha}">${row.RoslynSha}</a>`;
                        tr.appendChild(td);
                    }
                    table.appendChild(tr);
                }
                outputElement = table;
            }
            container.innerHTML = `<h1>${branch} ${build}</h1>`;
            container.appendChild(outputElement);
        })
        .catch((err) => console.error(err));
}
