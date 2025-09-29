
function add_proxydef(load_events)
    filename = "src/build_tools/proxylist.txt"

    table.insert(load_events, function(target, import, io)
        import("util", {rootdir="src/build_tools"})

        local tmpl_dir = path.join(path.directory(path.directory(filename)), "build_tools")

        local proxy_pragmas = ""
        local lib = "";
        local index = 1

        local funcs = io.readfile(filename):split("\n")
        for i, name in ipairs(funcs) do
            local func = string.trim(name)

            if func:match"[.]dll$" then
                lib = func:sub(0, -5)
            else
                proxy_pragmas = proxy_pragmas ..
                             format("#pragma comment(linker,\"/export:%s=C:\\\\Windows\\\\System32\\\\%s.%s,@%d\")\r\n",
                             func, lib, func, index)
                index = index + 1
            end
        end

        util.write_template(
            path.join(tmpl_dir, "proxy.c.in"),
            path.join("build", "proxy.c"),
            {
                PROXY_PRAGMAS = proxy_pragmas
            }
        )
    end)

    add_files("build/proxy.c")
end
