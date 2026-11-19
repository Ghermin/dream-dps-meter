-- Module du chargeur (essai) : 9e onglet « DPS meter » dans le menu Options (catégorie 7, Interface : connue du jeu,
-- sans onglet ni réglage) et nos réglages déplacés dans cette catégorie. Les tables d'onglets sont agrandies sur
-- l'instance du menu (hook sur guiAwake et setupTabData, avant la construction des onglets).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local CATEGORY = 7
local ICON = WDEV.tab_icon or 26 -- même icône que Paramètres de jeu pour commencer
local out = { log = {} }
local function note(s) table.insert(out.log, s); json.dump_file("wilds_tab_test.json", out) end

local cat_td = sdk.find_type_definition("app.GUI030100.CategoryData")

--- Copie le tableau d'onglets avec un onglet de plus (copie du premier, catégorie et icône changées).
local function extend(gui, name)
    local arr = gui:get_field(name)
    if not arr then return "absent" end
    local n = arr:get_size()
    for i = 0, n - 1 do
        if try(function() return arr:get_element(i):get_field("Category") end) == CATEGORY then return "déjà" end
    end
    local new = sdk.create_managed_array(cat_td, n + 1)
    new:add_ref_permanent()
    for i = 0, n - 1 do new[i] = arr:get_element(i) end
    local vt = arr:get_element(0)
    vt:set_field("Category", CATEGORY)
    vt:set_field("Icon", ICON)
    new[n] = vt
    gui:set_field(name, new)
    local check = gui:get_field(name)
    return string.format("ok %d -> %d, dernier = cat %s icône %s", n, check:get_size(),
        tostring(try(function() return check:get_element(n):get_field("Category") end)),
        tostring(try(function() return check:get_element(n):get_field("Icon") end)))
end

local function patch(gui, where)
    for _, name in ipairs({ "_TitleCategoryDataTable", "_InGameCategoryDataTable" }) do
        local ok, res = pcall(extend, gui, name)
        note(where .. " " .. name .. " : " .. tostring(ok and res or ("ERREUR " .. tostring(res))))
    end
end

-- nos réglages dans la catégorie 7
local vdm = sdk.get_managed_singleton("app.VariousDataManager")
local od = vdm:get_field("_Setting"):get_field("_MenuSetting"):get_field("_OptionData")
local tbl = od:get_field("_DataTable")
for id = 394, 399 do
    local e = tbl:get_element(id)
    if e then e:set_field("_Category", CATEGORY); e:set_field("<Category>k__BackingField", CATEGORY) end
end
note("réglages 394-399 -> catégorie 7")

local td = sdk.find_type_definition("app.GUI030100")
for _, sig in ipairs({ "guiAwake()", "setupTabData()" }) do
    local key = "tabtest." .. sig
    if not WDEV.hooks[key] then
        WDEV.hooks[key] = true
        local m = td:get_method(sig)
        sdk.hook(m, function(args)
            local gui = sdk.to_managed_object(args[2])
            if WDEV.tab_patch then pcall(WDEV.tab_patch, gui, sig) end
        end, function(r) return r end)
    end
end
WDEV.tab_patch = patch
-- instance déjà ouverte : tables agrandies tout de suite (prises en compte à la prochaine construction des onglets)
if WDEV.options_gui then patch(WDEV.options_gui, "instance") end
return { run = function() end }
