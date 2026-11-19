-- Module du chargeur (aperçu) : à chaque ouverture du menu Options, les 9 onglets de l'instance du menu prennent les
-- icônes base .. base+8 (app.IconDef.FACILITY). Instance seulement : rien d'enregistré, recréée à chaque ouverture.
local cmd = json.load_file("wilds_dev.json") or {}
local base = cmd.base or 0
WDEV.tab_patch = function(gui, where)
    if where ~= "guiAwake()" then return end
    for _, name in ipairs({ "_TitleCategoryDataTable", "_InGameCategoryDataTable" }) do
        local arr = gui:get_field(name)
        local n = arr:get_size()
        local has7 = false
        for i = 0, n - 1 do if arr:get_element(i):get_field("Category") == 7 then has7 = true end end
        local td = sdk.find_type_definition("app.GUI030100.CategoryData")
        if not has7 then
            local new = sdk.create_managed_array(td, n + 1); new:add_ref_permanent()
            for i = 0, n - 1 do new[i] = arr:get_element(i) end
            local vt = arr:get_element(0); vt:set_field("Category", 7); new[n] = vt
            gui:set_field(name, new); arr = new; n = n + 1
        end
        for i = 0, n - 1 do
            local vt = arr:get_element(i)
            vt:set_field("Icon", math.min(base + i, 121))
            arr[i] = vt
        end
    end
end
return { run = function() log.info("[Icon preview] base " .. base) end }
