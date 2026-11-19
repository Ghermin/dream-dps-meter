-- Chargeur de développement (jamais publié) : recharge un module de test sans « Reset scripts ».
-- Toutes les secondes, lit reframework\data\wilds_dev.json : { "gen": n, "module": "wilds_dev.xxx" }.
-- Quand gen change : le module est rechargé (require), puis M.run() est appelé une fois ; M.frame(), s'il existe,
-- est appelé à chaque image. Les hooks posés par un module persistent : un module doit les poser une seule fois
-- (registre global WDEV) et passer par WDEV.current pour que la dernière version du code soit utilisée.
WDEV = WDEV or { hooks = {}, current = nil }
local gen, last_check, mod = nil, 0, nil

local function load(name)
    package.loaded[name] = nil
    local ok, m = pcall(require, name)
    if not ok then log.error("[Wilds Dev] chargement de " .. name .. " : " .. tostring(m)) return nil end
    WDEV.current = m
    if type(m) == "table" and m.run then
        local ok2, err = pcall(m.run)
        if not ok2 then log.error("[Wilds Dev] " .. name .. ".run : " .. tostring(err)) end
    end
    log.info("[Wilds Dev] " .. name .. " chargé")
    return m
end

re.on_frame(function()
    local t = os.clock()
    if t - last_check >= 1 then
        last_check = t
        local cmd = json.load_file("wilds_dev.json")
        if type(cmd) == "table" and cmd.gen ~= gen and type(cmd.module) == "string" then
            gen = cmd.gen
            mod = load(cmd.module)
        end
    end
    if type(mod) == "table" and mod.frame then
        local ok, err = pcall(mod.frame)
        if not ok then log.error("[Wilds Dev] frame : " .. tostring(err)); mod = nil end
    end
end)
log.info("[Wilds Dev] chargeur prêt")
