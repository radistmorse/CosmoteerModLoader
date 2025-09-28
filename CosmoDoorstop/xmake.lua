includes("info.lua")
local info = build_info(info_lua)

add_rules("mode.debug", "mode.release")

option("include_logging")
    set_showmenu(true)
    set_description("Include verbose logging on run")
    add_defines("VERBOSE")


target("cosmodoorstop")
    set_kind("shared")
    set_optimize("smallest")
    set_basename("winmm")
    add_options("include_logging")
    local load_events = {}

	includes("src/build_tools/proxygen.lua")
	add_proxydef(load_events)

	includes("src/build_tools/rcgen.lua")
	add_rc(load_events, info)

	add_files("src/*.c")
	add_defines("UNICODE")
	add_links("shell32", "kernel32", "user32")

    add_cxflags("-GS-", "-Ob2", "-MT", "-GL-", "-FS")
    add_shflags("-nodefaultlib",
                "-entry:DllEntry",
                "-dynamicbase:no",
                {force=true})

    on_load(function(target)
        for i, event in ipairs(load_events) do
            event(target, import, io)
        end
    end)

target("unmanaged")
    set_kind("shared")
    set_optimize("smallest")
    local load_events = {}

   	includes("src/build_tools/rcgen.lua")
	add_rc(load_events, info)

	add_files("src/unmanaged.c")
    add_files("src/unmanaged.def")
	add_defines("STANDALONE")

    add_cxflags("-GS-", "-Ob2", "-MT", "-GL-", "-FS")
    add_shflags("-nodefaultlib",
                "-entry:DllEntry",
                {force=true})
    
    on_load(function(target)
        for i, event in ipairs(load_events) do
            event(target, import, io)
        end
    end)
