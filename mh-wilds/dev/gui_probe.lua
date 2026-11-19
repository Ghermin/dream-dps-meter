-- Module du chargeur (sonde, lecture seule) : description des types d'interface du menu Options (GUI030100…).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local function tn(td) return td and try(function() return td:get_full_name() end) or "?" end
local function describe(name)
    local td = sdk.find_type_definition(name)
    if not td then return false end
    local t = { parent = tn(td:get_parent_type()), fields = {}, methods = {} }
    for _, f in ipairs(td:get_fields() or {}) do
        table.insert(t.fields, (f:is_static() and "static " or "") .. (try(function() return f:get_type():get_full_name() end) or "?") .. " " .. f:get_name())
    end
    for _, m in ipairs(td:get_methods() or {}) do
        local ps = {}
        for _, p in ipairs(try(function() return m:get_param_types() end) or {}) do table.insert(ps, tn(p)) end
        table.insert(t.methods, (try(function() return m:get_return_type():get_full_name() end) or "?") .. " " .. m:get_name() .. "(" .. table.concat(ps, ", ") .. ")")
    end
    return t
end
return { run = function()
    local out = {}
    for _, n in ipairs({ "app.GUI030100.CategoryData", "app.GUI030100.cWindow",
        }) do
        out[n] = describe(n)
    end
    json.dump_file("wilds_gui_probe.json", out)
end }
