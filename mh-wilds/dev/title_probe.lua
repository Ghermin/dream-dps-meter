-- Module du chargeur (sonde, lecture seule) : textes de l'interface du menu Options (nom, chemin, GUID).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local out = { texts = {} }
local text_type, node_type = sdk.typeof("via.gui.Text"), sdk.typeof("via.gui.PlayObject")
local function walk(ctrl, path, depth)
    if depth > 9 or not ctrl or #out.texts > 400 then return end
    local name = try(function() return ctrl:call("get_Name") end) or "?"
    local p = path .. "/" .. name
    local tdn = try(function() return ctrl:get_type_definition():get_full_name() end)
    if tdn == "via.gui.Text" then
        table.insert(out.texts, { path = p, id = try(function() return ctrl:call("get_MessageId"):call("ToString()") end),
            msg = try(function() return ctrl:call("get_Message") end) })
    end
    local kids = try(function() return ctrl:call("getChildren(System.Type)", node_type) end)
    for i = 0, (kids and kids:get_size() or 0) - 1 do walk(kids:get_element(i), p, depth + 1) end
end
return { run = function()
    local gui = WDEV.options_gui
    local root = gui and gui:get_field("_CtrlTab"):get_field("_RootCtrl")
    walk(root, "", 0)
    json.dump_file("wilds_title_probe.json", out)
end }
