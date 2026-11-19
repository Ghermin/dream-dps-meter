-- Module du chargeur (sonde, lecture seule) : méthodes de app.GUI030100, et contenu des tables d'onglets de
-- l'instance du menu Options (capturée par un hook qui ne modifie rien sur guiLateUpdate).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local function tn(td) return td and try(function() return td:get_full_name() end) or "?" end
local out = { methods = {} }
local td = sdk.find_type_definition("app.GUI030100")
for _, m in ipairs(td:get_methods() or {}) do
    local ps = {}
    for _, p in ipairs(try(function() return m:get_param_types() end) or {}) do table.insert(ps, tn(p)) end
    table.insert(out.methods, (try(function() return m:get_return_type():get_full_name() end) or "?") .. " " .. m:get_name() .. "(" .. table.concat(ps, ", ") .. ")")
end
local function cat_table(gui, name)
    local arr = try(function() return gui:get_field(name) end)
    local res = {}
    for i = 0, (arr and arr:get_size() or 0) - 1 do
        local e = arr:get_element(i)
        table.insert(res, {
            category = try(function() return e:get_field("Category") end),
            icon = try(function() return e:get_field("Icon") end),
            msg = try(function() return e:get_field("MessageId"):call("ToString()") end),
        })
    end
    return res
end
local key = "tabprobe.GUI030100.guiLateUpdate"
if not WDEV.hooks[key] then
    WDEV.hooks[key] = true
    local m = td:get_method("guiLateUpdate()")
    if m then sdk.hook(m, function(args) WDEV.options_gui = sdk.to_managed_object(args[2]) end, function(r) return r end) end
end
local last = 0
return { run = function() end, frame = function()
    local t = os.clock()
    if t - last < 2 then return end
    last = t
    local gui = WDEV.options_gui
    if gui then
        out.title_tabs = cat_table(gui, "_TitleCategoryDataTable")
        out.ingame_tabs = cat_table(gui, "_InGameCategoryDataTable")
        local lists = try(function() return gui:get_field("_TabDispDataLists") end)
        out.tab_lists_len = lists and lists:get_size() or nil
        local lit = try(function() return gui:get_field("_ListIndexTable") end)
        out.list_index_len = lit and lit:get_size() or nil
        out.tab_index = try(function() return gui:get_field("_TabIndex") end)
        out.scene = try(function() return gui:get_field("_Scene") end)
    end
    json.dump_file("wilds_tab_probe.json", out)
end }
