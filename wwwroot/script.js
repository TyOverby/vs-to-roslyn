
let properties = {
    roslyn: {
        display_name: "Roslyn",
        short_name: "Roslyn",
        build_def: "1449",
        package: "Microsoft.CodeAnalysis.Compilers",
        github: "dotnet/roslyn",
    },
    lut: {
        display_name: "Live Unit Testing",
        short_name: "LUT",
        build_def: "2127",
        package: "Microsoft.CodeAnalysis.LiveUnitTesting",
        github: "dotnet/testimpact",
    },
    project_system: {
        display_name: "Managed Project System",
        short_name: "Project System",
        build_def: "1987",
        package: "Microsoft.VisualStudio.ProjectSystem.Managed",
        github: "dotnet/project-system",
    },
    fsharp: {
        display_name: "Visual F#",
        short_name: "F#",
        build_def: "5037",
        package: "Microsoft.FSharp",
        github: "Microsoft/visualfsharp",
    }
}

let builds = [];

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
                console.log(value);
                if (value.length == 0) {
                    alert("No elements!");
                } else {
                    for (let b of value) {
                        builds.push({
                            product: product_info.short_name,
                            branch: branch,
                            build: build,
                            github_url: product_info.github,
                            vso_build_tag: b.VsoBuildTag,
                            roslyn_build_tag: b.RoslynBuildTag,
                            roslyn_build_date: b.RoslynBuildDate,
                            github_sha: b.RoslynSha,
                        });
                    }
                }
                render()
            })
            .catch((err) => console.error(err));
    }
}

function render() {
    function td(value) {
        let td = document.createElement("td");
        if (typeof value === "string") {
            td.innerText = value;
        } else {
            td.appendChild(value);
        }
        return td;
    }
    function a(text, href) {
        let a = document.createElement("a");
        a.href = href;
        a.innerText = text;
        return a;
    }
    function th(value) {
        let th = document.createElement("th");
        th.innerText = value;
        return th;
    }

    function tr(children) {
        let tr = document.createElement("tr");
        for (let child of children) {
            tr.appendChild(child);
        }
        return tr;
    }

    let table = document.createElement("table");
    table.className = "table table-bordered table-striped";

    let thead = document.createElement("thead");
    table.appendChild(thead);
    let headers = ["Product", "Branch", "Build", "VSO Build Tag", "Roslyn Build Tag", "Roslyn Build Date", "Github SHA"];
    thead.appendChild(tr(headers.map(th)));

    let tbody = document.createElement("tbody");
    table.appendChild(tbody);
    for (var row of builds) {
        tbody.appendChild(tr([
            td(row.product),
            td(row.branch),
            td(row.build),
            td(row.vso_build_tag),
            td(row.roslyn_build_tag),
            td(row.roslyn_build_date),
            td(a(row.github_sha, `https://github.com/${row.github_url}/commit/${row.github_sha}`))
        ]));
    }

    let output = document.querySelector("#output");
    output.innerHTML = ""; // clear inside
    output.appendChild(table);
}
