
let properties = {
    roslyn: {
        display_name: "Roslyn",
        short_name: "Roslyn",
        build_def: "Roslyn-Signed",
        package: "Microsoft.CodeAnalysis.Compilers",
        github: "dotnet/roslyn",
    },
    lut: {
        display_name: "Live Unit Testing",
        short_name: "LUT",
        build_def: "TestImpact-Signed",
        package: "Microsoft.CodeAnalysis.LiveUnitTesting",
        github: "dotnet/testimpact",
    },
    project_system: {
        display_name: "Managed Project System",
        short_name: "Project System",
        build_def: "DotNet-Project-System",
        package: "Microsoft.VisualStudio.ProjectSystem.Managed",
        github: "dotnet/project-system",
    }
}

function process(form) {
    let global_output = document.querySelector("#output");
    let product = form.querySelector("#product").value;

    if (product === "all") {
        for (let key in properties) {
            run(key);
        }
    } else {
        run(product);
    }

    function run(product) {
        let branch = form.querySelector("#branch").value;
        let build = form.querySelector("#build").value;

        let product_info = properties[product];

        let container = document.createElement("div");
        container.innerHTML = `<h1>${product_info.display_name} ${branch} ${build}</h1>loading.gif`;

        global_output.appendChild(container);

        fetch(`/api/${product_info.build_def}/${product_info.package}/${branch}/${build}`)
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
                        td.innerText = "Vso Build Tag";
                        tr.appendChild(td);
                    }
                    {
                        let td = document.createElement("td");
                        td.innerHTML = `${product_info.short_name} Build Tag`;
                        tr.appendChild(td);
                    }
                    {
                        let td = document.createElement("td");
                        td.innerText = `${product_info.short_name} Sha`;
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
                            td.innerHTML = `<a href="https://github.com/${product_info.github}/commit/${row.RoslynSha}">${row.RoslynSha}</a>`;
                            tr.appendChild(td);
                        }
                        table.appendChild(tr);
                    }
                    outputElement = table;
                }
                container.innerHTML = `<h1>${product_info.display_name} ${branch} ${build}</h1>`;
                container.appendChild(outputElement);
            })
            .catch((err) => console.error(err));
    }
}
