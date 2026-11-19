-- Sonde de développement (pas publiée), v7 : état instantané des écrans. Toutes les 0,5 s, écrit la valeur courante
-- des propriétés de LECTURE (get_Xxx / isXxx / IsXxx, sans paramètre, renvoyant un booléen) de quelques gestionnaires
-- dans reframework\data\wilds_dps_probe.json, pour comparer deux écrans (chasse / fin de quête…).
--
-- ATTENTION : ne jamais appeler une méthode qui n'est pas une lecture. La v6 appelait toute méthode sans paramètre
-- et a exécuté app.MissionManager.setActiveQuestLayout en pleine quête (partie déréglée, jeu à relancer).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end

local SINGLETONS = { "app.GUIManager", "app.PauseManager", "app.GameFlowManager" }
-- verbes d'action : jamais appelés, même s'ils commencent par is/get
local FORBIDDEN = { "set", "request", "reset", "clear", "open", "close", "start", "end", "update", "init", "create",
    "change", "add", "remove", "apply", "exec", "call", "force", "enable", "disable", "load", "unload", "release" }

local function is_reader(n)
    if not (n:match("^get_[A-Z]") or n:match("^is[A-Z]") or n:match("^Is[A-Z]")) then return false end
    local lower = n:lower()
    for _, verb in ipairs(FORBIDDEN) do
        if lower:sub(1, #verb) == verb then return false end
    end
    return true
end

local methods = {}
for _, name in ipairs(SINGLETONS) do
    local td = sdk.find_type_definition(name)
    while td do
        for _, m in ipairs(td:get_methods() or {}) do
            local n = m:get_name()
            local ret = try(function() return m:get_return_type():get_full_name() end) or ""
            if is_reader(n) and m:get_num_params() == 0 and not try(m.is_static, m) and ret == "System.Boolean" then
                table.insert(methods, { singleton = name, m = m, label = name .. "." .. n })
            end
        end
        td = td:get_parent_type()
        if td and td:get_full_name():match("^System%.") then break end
    end
end

local last = 0
re.on_frame(function()
    local t = os.clock()
    if t - last < 0.5 then return end
    last = t
    local snap = {}
    for _, e in ipairs(methods) do
        local obj = sdk.get_managed_singleton(e.singleton)
        local v = obj and try(function() return e.m:call(obj) end)
        if type(v) == "boolean" then snap[e.label] = v end
    end
    json.dump_file("wilds_dps_probe.json", { t = t, snap = snap })
end)
log.info("[Wilds DPS Probe] v7 chargée (lecture seule), " .. #methods .. " propriétés")
