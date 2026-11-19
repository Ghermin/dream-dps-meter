-- Module du chargeur (sonde, lecture seule) : éléments (positions) de la ScrollList des onglets du menu Options.
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local function tn(o) return o and try(function() return o:get_type_definition():get_full_name() end) or "nil" end
local out = {}
local function methods_of(name, filter)
    local res = {}
    local td = sdk.find_type_definition(name)
    while td do
        for _, m in ipairs(td:get_methods() or {}) do
            local n = m:get_name()
            if not filter or n:find(filter) then table.insert(res, td:get_full_name() .. "." .. n .. "/" .. m:get_num_params()) end
        end
        td = td:get_parent_type()
        if td and td:get_full_name():match("^System%.") then break end
    end
    return res
end
out.scrolllist_methods = methods_of("via.gui.ScrollList", "Item")
out.transform_methods = methods_of("via.gui.TransformObject", "Position")
local function vec(p) return p and { x = try(function() return p.x end), y = try(function() return p.y end) } or nil end
return { run = function()
    local gui = WDEV.options_gui
    local ctrl = gui and gui:get_field("_CtrlTab")
    local list = ctrl and ctrl:get_field("_ScrList")
    out.list_type = tn(list)
    out.item_count = list and try(function() return list:call("get_ItemCount") end)
    local items = list and try(function() return list:call("get_Items") end)
    out.items_type = tn(items)
    local res = {}
    for i = 0, (items and items:get_size() or 0) - 1 do
        local it = items:get_element(i)
        table.insert(res, { name = try(function() return it:call("get_Name") end),
            pos = vec(try(function() return it:call("get_Position") end)),
            visible = try(function() return it:call("get_Visible") end) })
    end
    out.items = res
    out.list_pos = vec(list and try(function() return list:call("get_Position") end))
    json.dump_file("wilds_tabctrl_probe.json", out)
end }
