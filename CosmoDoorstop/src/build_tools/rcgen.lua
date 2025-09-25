function add_rc(load_events, info)
   table.insert(load_events, function(target, import, io)
      import("util", {rootdir="src/build_tools"})
      
      local tmpl_dir = "src/build_tools"

      util.write_template(
         path.join(tmpl_dir, "info.rc.in"),
         path.join("build", "info.rc"),
         {
            NAME = info.name,
            DESCRIPTION = info.description,
            MAJOR = info.version.major,
            MINOR = info.version.minor,
            PATCH = info.version.patch,
            RELEASE = info.version.release,
         }
      )
   end)

   add_files("build/info.rc")
end