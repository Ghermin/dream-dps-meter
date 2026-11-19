-- Module du chargeur de développement : installe la section « DPS meter » du menu Options (réessaie chaque seconde
-- tant que les données du jeu ne sont pas chargées) et écrit l'état dans reframework\data\wilds_options_test.json.
package.loaded["wilds_dps_meter.options_menu"] = nil
local menu = require("wilds_dps_meter.options_menu")

local DEFS = {
    { id = 398, title = "Afficher le DPS meter", help = "Afficher le panneau des dégâts en combat.", choices = { "Oui", "Non" }, default = 0 },
    { id = 397, title = "Dans les menus du jeu", help = "Masquer le panneau quand un menu du jeu est ouvert.", choices = { "Masquer", "Afficher" }, default = 0 },
    { id = 396, title = "Taille", help = "Taille du panneau des dégâts.", choices = { "Petite", "Normale", "Grande", "Très grande" }, default = 1 },
    { id = 395, title = "Fond", help = "Fond sombre derrière le panneau.", choices = { "Aucun", "Léger", "Moyen", "Fort" }, default = 0 },
    { id = 394, title = "Mode démo", help = "Afficher des dégâts fictifs pour voir le panneau sans combat.", choices = { "Désactivé", "Activé" }, default = 0 },
}

local done, next_try, last_dump = false, 0, 0

local function dump(extra)
    local values = {}
    for _, d in ipairs(DEFS) do values[tostring(d.id)] = menu.get(d.id) end
    json.dump_file("wilds_options_test.json", { installed = done, values = values, note = extra })
end

return {
    run = function() end,
    frame = function()
        local t = os.clock()
        if not done and t >= next_try then
            next_try = t + 1
            local ok, res = pcall(menu.install, DEFS, "DPS meter", "Réglages du DPS meter (Wilds DPS Meter).")
            if not ok then log.error("[Options test] " .. tostring(res)); dump(tostring(res)); done = true
            elseif res then done = true; dump() end
        end
        if done and t - last_dump > 2 then last_dump = t; dump() end
    end,
}
