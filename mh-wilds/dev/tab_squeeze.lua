-- Module du chargeur (essai) : resserre les icônes d'onglets du menu Options ouvert pour que 9 tiennent dans la
-- largeur prévue pour 8 (positions de 0 à celle du 8e).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local out = {}
return { run = function()
    local gui = WDEV.options_gui
    local list = gui and gui:get_field("_CtrlTab"):get_field("_ScrList")
    local items = list and list:call("get_Items")
    local n = list and list:call("get_ItemCount") or 0
    if n <= 8 then out.note = "8 onglets ou moins : rien à faire" json.dump_file("wilds_tab_squeeze.json", out) return end
    local span = items:get_element(7):call("get_Position").x - items:get_element(0):call("get_Position").x
    local step = span / (n - 1)
    out.step = step
    for i = 0, n - 1 do
        local it = items:get_element(i)
        local p = it:call("get_Position")
        local ok, err = pcall(function() it:call("set_Position", Vector3f.new(i * step, p.y, 0)) end)
        out[it:call("get_Name")] = ok and (i * step) or ("ERREUR " .. tostring(err))
    end
    json.dump_file("wilds_tab_squeeze.json", out)
end }
