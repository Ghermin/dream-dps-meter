-- Wilds DPS Meter : dégâts par chasseur et DPS du combat, pour Monster Hunter Wilds (script REFramework).
--
-- Principe : hook sur app.EnemyCharacter.evHit_Damage(app.HitInfo), appelé à chaque coup reçu par un monstre.
-- Après l'appel, HitInfo.DamageData.FinalDamage contient les dégâts du coup ; ils sont crédités à l'attaquant
-- (HitInfo:getActualAttackOwner(), qui renvoie le chasseur même pour un projectile ou un Palico).
--
-- Affichage : un panneau dessiné à la main (pas de fenêtre ImGui visible) dans le style du HUD du jeu.
-- Menu REFramework (Inser) : réglages, mode démo, journal des coups (reframework\data\wilds_dps_meter_debug.json).

local VERSION = "0.1.1"
local CONFIG_FILE = "wilds_dps_meter.json"
local DEBUG_FILE = "wilds_dps_meter_debug.json"
local TITLE = "Wilds DPS Meter"

-- ---------------------------------------------------------------- réglages

local cfg = {
    show = true,     -- panneau affiché
    hide_menus = true, -- panneau masqué quand un menu du jeu est ouvert (menu Start, carte…)
    idle = 120,      -- secondes sans coup avant de clore le combat (monstre capturé, quête quittée…)
    scale = 1.0,     -- taille du panneau (en plus de l'adaptation à la résolution)
    opacity = 0.0,   -- opacité du fond (0 : texte seul, comme le HUD du jeu)
    x = -1, y = -1,  -- coin bas gauche du panneau (-1 : en bas à droite, au-dessus de la barre d'objets)
    debug = false,   -- journal des coups
    demo = false,    -- mode démo actif (gardé d'un lancement à l'autre pour tester l'affichage)
}

do
    local ok, saved = pcall(json.load_file, CONFIG_FILE)
    if ok and type(saved) == "table" then
        for k, v in pairs(saved) do
            if cfg[k] ~= nil and type(v) == type(cfg[k]) then cfg[k] = v end
        end
    end
end

local function save_cfg()
    pcall(json.dump_file, CONFIG_FILE, cfg)
end

-- ---------------------------------------------------------------- outils

local function now()
    -- Sous Windows, clock() mesure le temps réel écoulé depuis le lancement du jeu.
    return os.clock()
end

local function try(f, ...)
    local ok, r = pcall(f, ...)
    if ok then return r end
    return nil
end

local function address(obj)
    return obj and try(obj.get_address, obj) or nil
end

--- 28043 -> "28 043" (espace fine insécable remplacée par une espace simple : la police d'ImGui n'a pas tous les glyphes).
local function fmt_int(n)
    local s = string.format("%d", math.floor(n + 0.5))
    local neg = s:sub(1, 1) == "-"
    if neg then s = s:sub(2) end
    local out = s:reverse():gsub("(%d%d%d)", "%1 "):reverse():gsub("^ ", "")
    return (neg and "-" or "") .. out
end

local function fmt_pct(p)
    if p >= 10 then return string.format("%.0f%%", p) end
    return (string.format("%.1f%%", p):gsub("%.", ","))
end

local function fmt_time(sec)
    sec = math.max(0, math.floor(sec))
    return string.format("%d:%02d", sec // 60, sec % 60)
end

-- Types connus du jeu. Seul app.HunterCharacter est confirmé ; les autres sont des candidats à vérifier avec le journal.
local KINDS = {
    { type = "app.HunterCharacter", kind = "hunter" },
    { type = "app.OtomoCharacter", kind = "otomo" },
    { type = "app.PorterCharacter", kind = "seikret" },
    { type = "app.NpcCharacter", kind = "npc" },
}
for _, k in ipairs(KINDS) do
    k.runtime = try(sdk.typeof, k.type)
end

local function component(go, runtime_type)
    if not runtime_type then return nil end
    return try(go.call, go, "getComponent(System.Type)", runtime_type)
end

local function health_of(enemy)
    local mgr = try(enemy.call, enemy, "get_HealthMgr")
    return mgr and try(mgr.call, mgr, "get_Health") or nil
end

-- Altérations : champ d'accumulation dans le calcul d'un coup (cCalcDamage), suffixe du type de jauge du monstre
-- (app.cEnemyBadCondition<Suffixe>), libellé, couleur. Chacune ne s'affiche que si elle a servi pendant le combat.
local STATUSES = {
    { key = "poison", field = "Poison", cond = { "Poison" }, label = "Poison", color = { 164, 92, 214 } },
    { key = "paralyse", field = "Paralyse", cond = { "Paralyse" }, label = "Paralysie", color = { 222, 204, 74 } },
    { key = "sleep", field = "Sleep", cond = { "Sleep" }, label = "Sommeil", color = { 120, 150, 230 } },
    { key = "blast", field = "Blast", cond = { "Blast" }, label = "Nitro", color = { 250, 158, 52 } },
    { key = "stun", field = "Stun", cond = { "Stun" }, label = "Étourdissement", color = { 236, 232, 220 } },
    { key = "stamina", field = "Stamina", cond = { "Stamina", "Tired" }, label = "Épuisement", color = { 110, 196, 150 } },
    { key = "ride", field = "Ride", cond = { "Ride", "Riding", "Mount" }, label = "Monte", color = { 236, 122, 172 } },
}

local function empty_buildup()
    local b = {}
    for _, st in ipairs(STATUSES) do b[st.key] = 0 end
    return b
end

-- Numéro de l'image en cours (incrémenté dans re.on_frame).
local frame_no = 0

local function master_player_object()
    local pm = sdk.get_managed_singleton("app.PlayerManager")
    if not pm then return nil end
    local info = try(pm.call, pm, "getMasterPlayer")
    return info and try(info.call, info, "get_Object") or nil
end

-- ---------------------------------------------------------------- combat en cours

-- Un combat = les grands monstres touchés depuis son début (targets). Il se termine quand ils sont tous morts
-- (chrono arrêté, coups suivants ignorés) ou après cfg.idle secondes sans coup (capture, quête quittée).
-- fight = { start, last, ended, total, rows = { [clé] = ligne }, order = { clés }, targets = { [adresse] = { dead } },
--           hunters = n, demo = bool }
-- ligne = { name, kind ("self" | "hunter" | "otomo" | "seikret" | "npc" | "other"), damage, hits, first, slot, pet }
local fight = nil
local debug_log = {}

local function new_fight(t)
    if fight and fight.targets then
        for _, tg in pairs(fight.targets) do
            if tg.obj then try(function() tg.obj:release() end) end
        end
    end
    fight = { start = t, last = t, total = 0, rows = {}, order = {}, targets = {}, hunters = 0, frame_hits = {} }
end

local function fight_over(t)
    return fight == nil or fight.ended ~= nil or t - fight.last > cfg.idle
end

local function fight_end(t)
    if fight.ended then return fight.ended end
    return fight_over(t) and fight.last or t
end

--- Grand monstre ? (EnemyCharacter._Context._Em:get_IsBoss(), comme les autres mods Wilds). Mis en cache par adresse.
local boss_cache, boss_cache_size = {}, 0
local function is_boss(enemy)
    local a = address(enemy)
    if not a then return false end
    local v = boss_cache[a]
    if v == nil then
        local holder = try(function() return enemy:get_field("_Context") end)
        local ctx = holder and try(function() return holder:get_field("_Em") end)
        v = ctx and try(function() return ctx:call("get_IsBoss") end) == true or false
        if boss_cache_size > 64 then boss_cache, boss_cache_size = {}, 0 end
        boss_cache[a] = v
        boss_cache_size = boss_cache_size + 1
    end
    return v
end

local function add_row(key, name, kind, t)
    -- elem : part élémentaire des coups ; poison, blast : dégâts d'altération ; buildup : accumulation d'altération
    -- (sert à répartir le poison et le nitro entre chasseurs) ; attr : élément de l'arme
    local row = { name = name, kind = kind, damage = 0, hits = 0, first = t, elem = 0, poison = 0, blast = 0,
        buildup = empty_buildup() }
    if kind == "hunter" or kind == "self" then
        fight.hunters = fight.hunters + 1
        row.slot = fight.hunters
    end
    fight.rows[key] = row
    table.insert(fight.order, key)
    return row
end

-- ---- Palico -> son chasseur
-- Le nom de la méthode qui donne le maître du Palico n'est pas connu : on essaie les getters sans paramètre de
-- app.OtomoCharacter dont le nom évoque le propriétaire, et on garde le premier qui renvoie un chasseur.
local T_HUNTER = KINDS[1].runtime
local T_OTOMO = KINDS[2].runtime
local OWNER_HINTS = { "Owner", "Master", "Hunter", "Player" }
local owner_getter, owner_candidates = nil, nil

local function as_game_object(o)
    -- les getters candidats renvoient aussi des booléens ou des vecteurs : seuls les objets du jeu nous intéressent
    if type(o) ~= "userdata" then return nil end
    local td = try(function() return o:get_type_definition() end)
    if not td then return nil end
    if td:get_full_name() == "via.GameObject" then return o end
    for _, getter in ipairs({ "get_GameObject", "get_Object" }) do
        local go = try(function() return o:call(getter) end)
        local t2 = type(go) == "userdata" and try(function() return go:get_type_definition() end) or nil
        if t2 and t2:get_full_name() == "via.GameObject" then return go end
    end
    local ch = try(function() return o:call("get_Character") end)
    return type(ch) == "userdata" and try(function() return ch:call("get_GameObject") end) or nil
end

local function palico_owner(go)
    local otomo = component(go, T_OTOMO)
    if not otomo then return nil end
    if not owner_candidates then
        owner_candidates = { "get_OwnerHunterCharacter" } -- vu en jeu sur app.OtomoCharacter
        owner_getter = nil
        local td = try(otomo.get_type_definition, otomo)
        for _, m in ipairs(td and td:get_methods() or {}) do
            local n = m:get_name()
            if n:sub(1, 4) == "get_" and m:get_num_params() == 0 and not try(m.is_static, m) then
                for _, hint in ipairs(OWNER_HINTS) do
                    if n:find(hint) then table.insert(owner_candidates, n); break end
                end
            end
        end
        log.info("[Wilds DPS Meter] getters candidats du Palico : " .. table.concat(owner_candidates, ", "))
    end
    for _, n in ipairs(owner_getter and { owner_getter } or owner_candidates) do
        local og = as_game_object(try(otomo.call, otomo, n))
        if og and component(og, T_HUNTER) then
            if owner_getter ~= n then
                owner_getter = n
                log.info("[Wilds DPS Meter] maître du Palico trouvé avec " .. n)
            end
            return og
        end
    end
    return nil
end

--- Clé, nom affiché et type d'un attaquant (GameObject).
local function identify_object(go)
    local key = address(go) or "?"
    local raw_name = try(go.call, go, "get_Name") or "?"

    local master = master_player_object()
    if master and address(master) == key then return key, "Toi", "self", raw_name end

    for _, k in ipairs(KINDS) do
        if component(go, k.runtime) then
            if k.kind == "hunter" then return key, nil, "hunter", raw_name end -- numéroté à la première apparition
            if k.kind == "otomo" then return key, "Palico", "otomo", raw_name end
            if k.kind == "seikret" then return key, "Seikret", "seikret", raw_name end
            if k.kind == "npc" then return key, "Chasseur de soutien", "npc", raw_name end
        end
    end
    return key, raw_name, "other", raw_name
end

--- Attaquant d'un coup : clé, nom, type, nom brut, Palico ?, GameObject du chasseur. nil pour le décor (« Gm… »,
--- éboulements, pièges) et tout ce qui n'est ni un chasseur ni son compagnon.
local function resolve_attacker(hit)
    local go = try(hit.call, hit, "getActualAttackOwner") or try(hit.call, hit, "get_AttackOwner")
    if not go then return nil end
    local key, name, kind, raw_name = identify_object(go)
    local pet = false
    if kind == "otomo" then
        -- dégâts du Palico comptés avec ceux de son chasseur (en solo, à défaut, avec les tiens)
        local owner = try(palico_owner, go)
        if not owner and (not fight or fight.hunters <= 1) then owner = master_player_object() end
        if not owner then return nil end
        go = owner
        key, name, kind = identify_object(owner)
        pet = true
    end
    if kind == "other" then return nil end
    return key, name, kind, raw_name, pet, go
end

--- Monstre du combat encore en vie (pas un petit monstre, pas un cadavre).
local function live_target(ea)
    local target = fight and fight.targets[ea]
    return target and not target.dead
end

local function credit(hit, damage, enemy)
    if fight and fight.demo then return end
    if not is_boss(enemy) then return end -- petits monstres ignorés
    local ea = address(enemy)
    local t = now()
    local target = fight and fight.targets[ea]
    if target and target.dead then return end -- coups sur un monstre déjà mort
    local key, name, kind, raw_name, pet = resolve_attacker(hit)
    if not key then return end
    if fight_over(t) then new_fight(t) end
    if not fight.targets[ea] then
        try(function() enemy:add_ref() end) -- gardé d'une image à l'autre pour surveiller ses PV
        fight.targets[ea] = { dead = false, obj = enemy, damage = 0 }
    end
    fight.last = t

    local row = fight.rows[key]
    if not row then
        row = add_row(key, name or "", kind, t)
        if name == nil then row.name = "Chasseur " .. row.slot end
    end
    row.damage = row.damage + damage
    row.hits = row.hits + 1
    if pet then row.pet = (row.pet or 0) + damage end
    fight.total = fight.total + damage -- en dernier : le total reste toujours la somme des lignes
    fight.targets[ea].damage = fight.targets[ea].damage + damage

    -- evDamage suit dans la même image avec le même montant : noté pour ne pas le recompter comme altération
    local fh = fight.frame_hits[ea]
    if not fh or fh.frame ~= frame_no then fh = { frame = frame_no, list = {} }; fight.frame_hits[ea] = fh end
    table.insert(fh.list, damage)

    if cfg.debug then
        table.insert(debug_log, {
            t = string.format("%.2f", t - fight.start), who = row.name, object = raw_name, pet = pet or nil,
            enemy = ea, damage = damage,
        })
        if #debug_log > 2000 then table.remove(debug_log, 1) end
    end
end

--- Ce montant correspond-il à un coup déjà compté sur ce monstre dans cette image ? (et le retire de la liste)
local function consume_hit(ea, damage)
    local fh = fight.frame_hits[ea]
    if not fh or fh.frame ~= frame_no then return false end
    for i, d in ipairs(fh.list) do
        if math.abs(d - damage) < 0.05 then table.remove(fh.list, i); return true end
    end
    return false
end

--- Nom d'une valeur d'enum lue dans un champ (ex : DamageType -> "POISON"). Tables mises en cache par type d'enum.
local enum_maps = {}
local function enum_name(obj, field_name)
    local td = obj:get_type_definition()
    local f = td and td:get_field(field_name)
    local etd = f and f:get_type()
    if not etd then return nil end
    local full = etd:get_full_name()
    local map = enum_maps[full]
    if not map then
        map = {}
        for _, sf in ipairs(etd:get_fields() or {}) do
            if sf:is_static() then
                local v = try(sf.get_data, sf, nil)
                if type(v) == "number" then map[v] = sf:get_name() end
            end
        end
        enum_maps[full] = map
    end
    return map[obj:get_field(field_name)]
end

--- Élément de l'arme d'un chasseur ("FIRE", "WATER", "ELEC", "ICE", "DRAGON", "POISON", "BLAST"…) ou nil.
local function weapon_attr(go)
    local hunter = go and component(go, T_HUNTER)
    local status = hunter and try(function() return hunter:call("get_HunterStatus") end)
    local attack = status and try(function() return status:call("get_AttackPower") end)
    if not attack then return nil end
    local v = try(function() return attack:call("get_AttibuteType") end) -- sic, nom du jeu
    if type(v) ~= "number" then return nil end
    local map = enum_maps["app.WeaponDef.ATTR"]
    if not map then
        map = {}
        local etd = sdk.find_type_definition("app.WeaponDef.ATTR")
        for _, sf in ipairs(etd and etd:get_fields() or {}) do
            if sf:is_static() then
                local n = try(sf.get_data, sf, nil)
                if type(n) == "number" then map[n] = sf:get_name() end
            end
        end
        enum_maps["app.WeaponDef.ATTR"] = map
    end
    return map[v]
end

--- Dégâts d'altération (poison, nitro) répartis selon l'accumulation de chacun ; en solo, à toi.
local function credit_status(kind, damage)
    local total_b, contributors = 0, {}
    for _, row in pairs(fight.rows) do
        if row.buildup[kind] > 0 then
            total_b = total_b + row.buildup[kind]
            table.insert(contributors, row)
        end
    end
    if total_b == 0 then
        if fight.hunters ~= 1 then return end
        for _, row in pairs(fight.rows) do
            if row.slot then contributors, total_b = { row }, 1; row.buildup[kind] = 1 end
        end
        if total_b == 0 then return end
    end
    for _, row in ipairs(contributors) do
        local share = damage * row.buildup[kind] / total_b
        row.damage = row.damage + share
        row[kind] = row[kind] + share
        fight.total = fight.total + share
    end
    fight.last = now()
end

--- Mort des monstres du combat : PV <= 0 (vérifié à chaque image). Tous morts -> chrono arrêté, coups ignorés.
local function is_dead(enemy)
    local mgr = try(function() return enemy:call("get_HealthMgr") end)
    if not mgr then return false end
    if try(function() return mgr:call("get_IsDead") end) == true then return true end
    local hp = try(function() return mgr:call("get_Health") end)
    return type(hp) == "number" and hp <= 0
end

-- types de jauges vus (écrits une fois dans le journal du jeu, pour le diagnostic)
local seen_types, gauge_types_logged = {}, false

--- Lit une propriété : la méthode get_X si elle existe, sinon le champ X ou <X>k__BackingField (certaines
--- « propriétés » du jeu ne sont que des champs, ex : cEnemyContext.Conditions).
local function prop(obj, name)
    if obj == nil then return nil end
    local td = try(function() return obj:get_type_definition() end)
    if not td then return nil end
    local m = td:get_method(name)
    if m then return try(function() return m:call(obj) end) end
    local p = name:match("^get_(.+)$")
    if p then
        for _, f in ipairs({ p, "<" .. p .. ">k__BackingField" }) do
            if td:get_field(f) then return try(function() return obj:get_field(f) end) end
        end
    end
    return nil
end

--- Jauges d'altération d'un monstre : { [clé] = { ratio, active } } (ratio : jauge remplie, ou temps restant si active).
local function read_gauges(enemy)
    local out = {}
    local holder = try(function() return enemy:get_field("_Context") end)
    local ctx = holder and try(function() return holder:get_field("_Em") end)
    local conds = prop(ctx, "get_Conditions")
    local arr = prop(conds, "get__Conditions")
    local n = arr and try(function() return arr:get_size() end) or 0
    if not gauge_types_logged and n == 0 then
        gauge_types_logged = true
        log.info(string.format("[Wilds DPS Meter] jauges du monstre illisibles : _Context=%s _Em=%s Conditions=%s _Conditions=%s",
            tostring(holder ~= nil), tostring(ctx ~= nil), tostring(conds ~= nil), tostring(arr ~= nil)))
    end

    local function keep(key, ratio, active)
        if not ratio then return end
        local prev = out[key]
        if not prev or (active and not prev.active) or (active == prev.active and ratio > prev.ratio) then
            out[key] = { ratio = ratio, active = active }
        end
    end

    local function snapshot(e, cur_name, max_name, inverse)
        if prop(e, "get_IsValid") == false then return nil end
        local active = prop(e, "get_IsActive") == true
        local cur, max
        if active then
            cur, max = prop(e, "get_CurrentTimer"), prop(e, "get_ActivateTime")
            if type(cur) == "number" and type(max) == "number" and max > 0 then return math.max(0, 1 - cur / max), true end
            return nil
        end
        cur, max = prop(e, cur_name), prop(e, max_name)
        if type(cur) ~= "number" or type(max) ~= "number" or max <= 0 then return nil end
        local r = math.min(1, math.max(0, cur / max))
        if inverse then r = 1 - r end
        return r, false
    end

    for i = 0, n - 1 do
        local e = try(function() return arr:get_element(i) end)
        local td = e and try(function() return e:get_type_definition() end)
        -- le type réel peut dériver de app.cEnemyBadCondition<X> : on remonte les parents
        local names = {}
        local t = td
        for _ = 1, 6 do
            if not t then break end
            table.insert(names, t:get_full_name())
            t = t:get_parent_type()
        end
        if not gauge_types_logged and names[1] then seen_types[names[1]] = true end
        for _, st in ipairs(STATUSES) do
            local match = false
            for _, tname in ipairs(names) do
                for _, suffix in ipairs(st.cond) do
                    if tname == "app.cEnemyBadCondition" .. suffix or tname == "app.cEmCondition" .. suffix then match = true end
                end
            end
            if match then keep(st.key, snapshot(e, "get_Value", "get_LimitValue")) end
        end
    end

    -- Épuisement : jauge à part (Conditions.Tired), l'endurance du monstre baisse jusqu'au déclenchement
    if not out.stamina then
        local tired = prop(conds, "get_Tired")
        if tired then keep("stamina", snapshot(tired, "get_Stamina", "get_DefaultStamina", true)) end
    end
    return out
end

--- Altérations utilisées pendant le combat (accumulation par au moins un chasseur).
local function used_statuses()
    local used = {}
    if not fight then return used end
    for _, row in pairs(fight.rows) do
        for _, st in ipairs(STATUSES) do
            if row.buildup[st.key] > 0 then used[st.key] = true end
        end
    end
    return used
end

local gauges_next = 0
local gauge_error_logged = false
--- Jauges du monstre principal (le plus touché, encore en vie), relues 8 fois par seconde.
local function update_gauges(t)
    if not fight or fight.demo then return end
    if fight.ended then fight.gauges = {} return end
    if t < gauges_next then return end
    gauges_next = t + 0.125
    local best, best_dmg = nil, -1
    for _, tg in pairs(fight.targets) do
        if not tg.dead and tg.obj and tg.damage > best_dmg then best, best_dmg = tg, tg.damage end
    end
    fight.gauges = best and read_gauges(best.obj) or {}
    if best and not gauge_types_logged and next(seen_types) then
        local list = {}
        for k in pairs(seen_types) do table.insert(list, k) end
        table.sort(list)
        local found = {}
        for k in pairs(fight.gauges) do table.insert(found, k) end
        log.info("[Wilds DPS Meter] jauges du monstre : " .. table.concat(list, ", ") .. " | reconnues : " .. table.concat(found, ", "))
        gauge_types_logged = true
    end
end

local function check_deaths()
    if not fight or fight.ended or fight.demo then return end
    local any, alive = false, false
    for _, tg in pairs(fight.targets) do
        any = true
        if not tg.dead then
            if tg.obj and is_dead(tg.obj) then tg.dead = true else alive = true end
        end
    end
    if any and not alive then
        fight.ended = now()
        fight.last = fight.ended
    end
end

-- ---------------------------------------------------------------- mode démo (tester l'affichage sans combat)

local DEMO = {
    { key = "d1", name = "Toi", kind = "self", rate = 210, attr = "FIRE", elem = 0.22, buildup = { paralyse = 160, stun = 40 } },
    { key = "d2", name = "Chasseur 2", kind = "hunter", rate = 260, attr = "WATER", elem = 0.15, blast = 0.12,
        buildup = { blast = 300, stun = 60 } },
    { key = "d3", name = "Chasseur 3", kind = "hunter", rate = 150, attr = "ELEC", elem = 0.30, buildup = { paralyse = 90, ride = 120 } },
    { key = "d4", name = "Chasseur 4", kind = "hunter", rate = 95, poison = 0.25, buildup = { poison = 250 } },
}
local demo_next = 0

local function start_demo()
    local t = now()
    new_fight(t - 154)
    fight.demo = true
    fight.gauges = { paralyse = { ratio = 0.62 }, poison = { ratio = 0.45, active = true }, blast = { ratio = 0.3 },
        stun = { ratio = 0.8 }, ride = { ratio = 0.55 } }
    for _, d in ipairs(DEMO) do
        local row = add_row(d.key, d.name, d.kind, fight.start + math.random(0, 12))
        row.damage = d.rate * (t - row.first) * (0.85 + 0.3 * math.random())
        row.attr = d.attr
        row.elem = row.damage * (d.elem or 0)
        row.poison = row.damage * (d.poison or 0)
        row.blast = row.damage * (d.blast or 0)
        for k, v in pairs(d.buildup or {}) do row.buildup[k] = v end
        fight.total = fight.total + row.damage
    end
    fight.last = t
end

local function tick_demo(t)
    if not (fight and fight.demo) or t < demo_next then return end
    demo_next = t + 0.35
    local d = DEMO[math.random(1, #DEMO)]
    local dmg = d.rate * (0.6 + 1.2 * math.random()) * 1.4
    local row = fight.rows[d.key]
    row.damage = row.damage + dmg
    row.hits = row.hits + 1
    fight.total = fight.total + dmg
    fight.last = t
end

-- ---------------------------------------------------------------- hook des dégâts

-- Vérifié en jeu (sonde, solo) : après evHit_Damage, HitInfo.DamageData (app.cDamageParamEm).FinalDamage vaut
-- exactement la perte de PV appliquée ensuite par evDamage_Health (plusieurs coups d'une même image s'additionnent).
-- Les PV ne baissent pas pendant evHit_Damage : lire les PV avant/après ne donne rien.

local td_enemy = sdk.find_type_definition("app.EnemyCharacter")
local m_damage = td_enemy and td_enemy:get_method("evHit_Damage(app.HitInfo)")
local hook_ok = m_damage ~= nil
local T_ENEMY = try(sdk.typeof, "app.EnemyCharacter")

if hook_ok then
    sdk.hook(m_damage,
        function(args)
            local s = thread.get_hook_storage()
            s.enemy = sdk.to_managed_object(args[2])
            s.hit = s.enemy and sdk.to_managed_object(args[3]) or nil
        end,
        function(retval)
            local s = thread.get_hook_storage()
            local hit, enemy = s.hit, s.enemy
            if hit and enemy then
                local ok, err = pcall(function()
                    local dd = hit:call("get_DamageData")
                    local damage = dd and dd:get_field("FinalDamage") or 0
                    if not damage or damage <= 0 then return end
                    -- coups d'un monstre sur un autre (guerres de territoire) : ignorés
                    local owner = try(hit.call, hit, "getActualAttackOwner")
                    if owner and component(owner, T_ENEMY) then return end
                    credit(hit, damage, enemy)
                end)
                if not ok and cfg.debug then table.insert(debug_log, { error = tostring(err) }) end
            end
            return retval
        end)

    -- Détail d'un coup (vu en jeu) : app.cEnemyStockDamage.cCalcDamage a Physical, Element (dégâts) et Poison, Blast…
    -- (accumulation d'altération, pas des dégâts). Sert à colorer la barre ; le montant compté reste FinalDamage ci-dessus.
    local mark_td = sdk.find_type_definition("app.cEnemyStockDamage.mcEnemyHitMarkManager")
    local m_mark = mark_td and mark_td:get_method("playHitMarkEffect(app.cEnemyStockDamage.cCalcDamage, app.HitInfo)")
    if m_mark then
        sdk.hook(m_mark, function(args)
            if not fight or fight.demo or fight.ended then return end
            pcall(function()
                local calc = sdk.to_managed_object(args[3])
                local hit = sdk.to_managed_object(args[4])
                if not calc or not hit then return end
                local target_go = try(hit.call, hit, "get_DamageOwner")
                if not target_go or not live_target(address(component(target_go, T_ENEMY))) then return end
                local key, _, _, _, _, hunter_go = resolve_attacker(hit)
                local row = key and fight.rows[key]
                if not row then return end
                row.elem = row.elem + (calc:get_field("Element") or 0)
                for _, st in ipairs(STATUSES) do
                    local v = try(function() return calc:get_field(st.field) end)
                    if type(v) == "number" and v > 0 then row.buildup[st.key] = row.buildup[st.key] + v end
                end
                if not row.attr then row.attr = weapon_attr(hunter_go) end
            end)
        end, function(r) return r end)
    end

    -- Dégâts sans coup (poison, nitro) : evDamage(cApplyEventParam), reconnus au nom de leur DamageType et répartis
    -- entre chasseurs selon leur accumulation de cette altération. À valider en jeu avec une arme à poison / nitro.
    local m_apply
    for _, m in ipairs(td_enemy:get_methods()) do
        if m:get_name() == "evDamage" and m:get_num_params() == 1 then m_apply = m; break end
    end
    if m_apply then
        sdk.hook(m_apply, function(args)
            if not fight or fight.demo or fight.ended then return end
            pcall(function()
                local enemy = sdk.to_managed_object(args[2])
                if not enemy or not live_target(address(enemy)) then return end
                local p = sdk.to_managed_object(args[3])
                local damage = p and p:get_field("Damage") or 0
                if damage <= 0 then return end
                if consume_hit(address(enemy), damage) then return end -- part d'un coup déjà compté
                local dtype = enum_name(p, "DamageType")
                local kind = dtype and (dtype:find("POISON") and "poison" or (dtype:find("BLAST") or dtype:find("BOMB")) and "blast")
                if cfg.debug and dtype and dtype ~= "NONE" and dtype ~= "NORMAL" then
                    table.insert(debug_log, { status = dtype, custom = enum_name(p, "DamageTypeCustom"), damage = damage })
                end
                if kind then credit_status(kind, damage) end
            end)
        end, function(r) return r end)
    end
else
    log.warn("[Wilds DPS Meter] app.EnemyCharacter.evHit_Damage introuvable : le jeu a peut-être changé.")
end
log.info(string.format("[Wilds DPS Meter] v%s chargé, hook des dégâts : %s, types : %s", VERSION, hook_ok and "ok" or "ABSENT",
    (function()
        local t = {}
        for _, k in ipairs(KINDS) do table.insert(t, k.type .. "=" .. (k.runtime and "ok" or "absent")) end
        return table.concat(t, " ")
    end)()))

-- ---------------------------------------------------------------- panneau (style HUD de Wilds)

-- Couleurs ImGui : 0xAABBGGRR.
local function rgba(r, g, b, a)
    return ((math.floor((a or 1) * 255 + 0.5) & 0xFF) << 24) | (b << 16) | (g << 8) | r
end

local C = {
    text = { 236, 230, 214 },     -- blanc cassé du HUD
    muted = { 168, 160, 142 },
    accent = { 226, 189, 104 },   -- or des titres et sélections du jeu
    line = { 226, 189, 104 },
    shadow = { 0, 0, 0 },
    bg = { 12, 12, 11 },
    track = { 255, 255, 255 },
}
-- Couleurs d'équipe de la série (joueur 1 à 4), puis compagnons.
local SLOT_COLORS = { { 222, 84, 72 }, { 76, 146, 226 }, { 232, 196, 72 }, { 92, 186, 104 } }
local KIND_COLORS = { otomo = { 196, 158, 112 }, seikret = { 150, 196, 176 }, npc = { 170, 170, 186 }, other = { 150, 150, 150 } }
-- Couleurs des parts de la barre : élément de l'arme, poison, nitro.
local ATTR_COLORS = {
    FIRE = { 247, 128, 44 }, WATER = { 64, 160, 240 }, ELEC = { 246, 214, 64 }, ICE = { 150, 222, 246 },
    DRAGON = { 178, 64, 104 }, POISON = { 164, 92, 214 }, PARALYSE = { 214, 200, 70 }, SLEEP = { 120, 150, 220 },
    BLAST = { 250, 158, 52 },
}
local ELEMENT_FALLBACK = { 120, 200, 220 }
-- Part physique : gris neutre ; la couleur du chasseur est sur le trait à gauche du nom. Seuls l'élément et les
-- altérations sont en couleur.
local PHYSICAL_COLOR = { 150, 148, 142 }

--- Parts de la barre d'une ligne : { montant, couleur }, dans l'ordre physique, élément, poison, nitro.
local function bar_segments(r, player_color)
    local elem = math.min(r.elem or 0, r.damage)
    local poison, blast = r.poison or 0, r.blast or 0
    local phys = math.max(0, r.damage - elem - poison - blast)
    return {
        { phys, PHYSICAL_COLOR },
        { elem, r.attr and ATTR_COLORS[r.attr] or ELEMENT_FALLBACK },
        { poison, ATTR_COLORS.POISON },
        { blast, ATTR_COLORS.BLAST },
    }
end

local function col(c, a) return rgba(c[1], c[2], c[3], a or 1) end

local function row_color(r)
    if r.slot then return SLOT_COLORS[((r.slot - 1) % #SLOT_COLORS) + 1] end
    return KIND_COLORS[r.kind] or KIND_COLORS.other
end

local WINDOW_FLAGS = 1 | 2 | 8 | 16 | 32 | 128 | 256 | 4096 | 196608
-- NoTitleBar | NoResize | NoScrollbar | NoScrollWithMouse | NoCollapse | NoBackground | NoSavedSettings | NoFocusOnAppearing | NoNav

local dl, ox, oy, S -- draw list, origine du panneau, échelle

local function V(x, y) return Vector2f.new(ox + x * S, oy + y * S) end

local function text_w(str, size)
    imgui.push_font_size(size * S)
    local w = imgui.calc_text_size(str).x
    imgui.pop_font_size()
    return w / S
end

--- Texte avec ombre portée, comme le HUD. align : 0 gauche, 1 droite (x = bord droit).
local SHADOW_OFFSETS = { { -1, 0, 0.30 }, { 1, 0, 0.30 }, { 0, -1, 0.30 }, { 0, 1, 0.30 }, { 1.5, 1.5, 0.65 } }

local function draw_text(str, x, y, size, color, alpha, align)
    imgui.push_font_size(size * S)
    if align == 1 then x = x - imgui.calc_text_size(str).x / S end
    -- ombre douce comme les textes du HUD : un contour léger puis une ombre portée
    local sa = alpha or 1
    for _, o in ipairs(SHADOW_OFFSETS) do dl:add_text(V(x + o[1], y + o[2]), col(C.shadow, o[3] * sa), str) end
    dl:add_text(V(x, y), col(color, alpha), str)
    imgui.pop_font_size()
end

local function hline(x1, x2, y, color, alpha, thickness)
    dl:add_line(V(x1, y), V(x2, y), col(color, alpha), (thickness or 1) * S)
end

local mirror = false -- panneau dans la moitié droite de l'écran : ornement à droite, dégradés inversés

--- Rectangle en dégradé horizontal : opacité a_edge du côté du bord d'écran le plus proche, a_far de l'autre.
local function hgrad(x1, y1, x2, y2, c, a_edge, a_far)
    local l, r = a_edge, a_far
    if mirror then l, r = a_far, a_edge end
    dl:add_rect_filled_multi_color(V(x1, y1), V(x2, y2), col(c, l), col(c, r), col(c, r), col(c, l))
end

--- Fond et filets dorés : pleins du côté du bord d'écran, ils s'effacent vers le centre (comme les panneaux du HUD).
local function draw_frame(w, h)
    local a = cfg.opacity
    if a > 0 then hgrad(0, 0, w, h, C.bg, a, a * 0.55) end
    hgrad(0, 0, w, 1.5, C.line, 0.9, 0.05)
    -- losange d'angle, côté bord d'écran
    local cx, cy, r = mirror and w or 0, 0, 4
    dl:add_quad_filled(V(cx, cy - r), V(cx + r, cy), V(cx, cy + r), V(cx - r, cy), col(C.accent, 1))
end

local W, PAD = 360, 16
local ROW_H, HEAD_H = 36, 40

local GAUGE_LINE_H, STATUS_LINE_H = 20, 19

--- Petit picto d'altération façon HUD du jeu, dessiné à la main, centré en (cx, cy), environ 11 unités de haut.
local function draw_status_icon(key, cx, cy, c, alpha)
    local k = col(c, alpha or 1)
    local hole = col(C.shadow, 0.85 * (alpha or 1))
    local th = 1.5 * S
    if key == "poison" then -- crâne
        dl:add_circle_filled(V(cx, cy - 1), 4.4 * S, k, 16)
        dl:add_rect_filled(V(cx - 2.6, cy + 1.5), V(cx + 2.6, cy + 5), k, 1 * S, 0)
        dl:add_circle_filled(V(cx - 1.7, cy - 1), 1.2 * S, hole, 8)
        dl:add_circle_filled(V(cx + 1.7, cy - 1), 1.2 * S, hole, 8)
        dl:add_line(V(cx, cy + 2), V(cx, cy + 5), hole, 0.8 * S)
    elseif key == "paralyse" then -- éclair
        dl:add_triangle_filled(V(cx + 1.8, cy - 5.5), V(cx - 3.8, cy + 1), V(cx + 0.6, cy + 1), k)
        dl:add_triangle_filled(V(cx - 0.6, cy - 1), V(cx + 3.8, cy - 1), V(cx - 1.8, cy + 5.5), k)
    elseif key == "sleep" then -- Zz
        dl:add_line(V(cx - 5, cy - 1), V(cx + 0.5, cy - 1), k, th)
        dl:add_line(V(cx + 0.5, cy - 1), V(cx - 5, cy + 5), k, th)
        dl:add_line(V(cx - 5, cy + 5), V(cx + 0.5, cy + 5), k, th)
        dl:add_line(V(cx + 1.5, cy - 5), V(cx + 5, cy - 5), k, 1.2 * S)
        dl:add_line(V(cx + 5, cy - 5), V(cx + 1.5, cy - 1.5), k, 1.2 * S)
        dl:add_line(V(cx + 1.5, cy - 1.5), V(cx + 5, cy - 1.5), k, 1.2 * S)
    elseif key == "blast" then -- explosion : étoile à 8 branches et cœur clair
        dl:add_quad_filled(V(cx, cy - 6), V(cx + 3, cy), V(cx, cy + 6), V(cx - 3, cy), k)
        dl:add_quad_filled(V(cx - 6, cy), V(cx, cy - 3), V(cx + 6, cy), V(cx, cy + 3), k)
        dl:add_quad_filled(V(cx - 4.2, cy - 4.2), V(cx + 1.2, cy - 1.2), V(cx + 4.2, cy + 4.2), V(cx - 1.2, cy + 1.2), k)
        dl:add_quad_filled(V(cx + 4.2, cy - 4.2), V(cx + 1.2, cy + 1.2), V(cx - 4.2, cy + 4.2), V(cx - 1.2, cy - 1.2), k)
        dl:add_circle_filled(V(cx, cy), 1.8 * S, col({ 255, 238, 190 }, alpha or 1), 10)
    elseif key == "stun" then -- étoile à 5 branches
        local pts = {}
        for i = 0, 9 do
            local a = -math.pi / 2 + i * math.pi / 5
            local r = (i % 2 == 0) and 5.8 or 2.4
            pts[i] = { cx + r * math.cos(a), cy + 0.5 + r * math.sin(a) }
        end
        for i = 0, 9, 2 do
            local a, b, c2 = pts[i], pts[(i + 1) % 10], pts[(i + 9) % 10]
            dl:add_triangle_filled(V(a[1], a[2]), V(b[1], b[2]), V(c2[1], c2[2]), k)
        end
        dl:add_circle_filled(V(cx, cy + 0.5), 2.5 * S, k, 10)
    elseif key == "stamina" then -- goutte de sueur
        dl:add_circle_filled(V(cx, cy + 2), 3.4 * S, k, 16)
        dl:add_triangle_filled(V(cx - 2.95, cy + 0.3), V(cx + 2.95, cy + 0.3), V(cx, cy - 5.5), k)
        dl:add_circle_filled(V(cx - 1.1, cy + 1.6), 0.9 * S, col({ 255, 255, 255 }, 0.7 * (alpha or 1)), 8)
    elseif key == "ride" then -- double chevron
        dl:add_line(V(cx - 4.5, cy), V(cx, cy - 4.5), k, th)
        dl:add_line(V(cx, cy - 4.5), V(cx + 4.5, cy), k, th)
        dl:add_line(V(cx - 4.5, cy + 4.5), V(cx, cy), k, th)
        dl:add_line(V(cx, cy), V(cx + 4.5, cy + 4.5), k, th)
    end
end

--- Hauteur d'une ligne de texte à cette taille (unités du panneau), pour centrer les pictos sur le texte.
local function text_h(size)
    imgui.push_font_size(size * S)
    local hgt = imgui.calc_text_size("0%").y
    imgui.pop_font_size()
    return hgt / S
end

--- Jauges du monstre à afficher : altérations utilisées dans ce combat, dans l'ordre de STATUSES.
local function visible_gauges()
    local out = {}
    if not fight or not fight.gauges then return out end
    local used = fight.demo and nil or used_statuses()
    for _, st in ipairs(STATUSES) do
        local g = fight.gauges[st.key]
        if g and (fight.demo or used[st.key] or g.active) then table.insert(out, { st = st, g = g }) end
    end
    return out
end

--- Altérations appliquées par ce joueur : { { st, value (accumulation cumulée), pct (part du groupe) } }.
local function row_statuses(r)
    local out = {}
    for _, st in ipairs(STATUSES) do
        local mine = r.buildup and r.buildup[st.key] or 0
        if mine > 0 then
            local sum = 0
            for _, other in pairs(fight.rows) do sum = sum + (other.buildup and other.buildup[st.key] or 0) end
            table.insert(out, { st = st, value = mine, pct = 100 * mine / math.max(1e-6, sum) })
        end
    end
    return out
end

local function header_height()
    return HEAD_H + (#visible_gauges() > 0 and GAUGE_LINE_H or 0)
end

local function row_height(r)
    return ROW_H + (#row_statuses(r) > 0 and STATUS_LINE_H or 0)
end

local function layout_height()
    if not fight or #fight.order == 0 then return HEAD_H + 28 end
    local h = header_height()
    for _, k in ipairs(fight.order) do h = h + row_height(fight.rows[k]) end
    return h + 2
end

local function draw_panel(t)
    local rows = {}
    if fight then
        for _, k in ipairs(fight.order) do table.insert(rows, fight.rows[k]) end
        table.sort(rows, function(a, b) return a.damage > b.damage end)
    end
    local h = layout_height()
    draw_frame(W, h)

    -- En-tête : durée et DPS du groupe à gauche (à la place d'un titre), total à droite
    draw_text(fmt_int(fight and fight.total or 0), W - PAD, 6, 25, C.text, 1, 1)

    local sub
    if not hook_ok then
        sub = "Jeu non reconnu : mets à jour le DPS meter"
    elseif not fight then
        sub = "En attente du premier coup"
    else
        local dur = math.max(1, fight_end(t) - fight.start)
        sub = fmt_time(dur) .. "   ·   " .. fmt_int(fight.total / dur) .. " DPS" .. (fight_over(t) and "   ·   terminé" or "")
        if fight.demo then sub = sub .. "   ·   démo" end
    end
    draw_text(sub, PAD, 11, 17, hook_ok and C.accent or SLOT_COLORS[1])

    -- Jauges d'altération du monstre principal : un pourcentage coloré par altération utilisée dans ce combat.
    -- Altération en cours : point devant, pourcentage = temps restant.
    local gx, gy = PAD, HEAD_H - 4
    for _, item in ipairs(visible_gauges()) do
        local st, g = item.st, item.g
        local label = string.format("%.0f%%", 100 * g.ratio)
        local icy = gy + text_h(15) / 2
        if g.active then -- altération en cours : halo derrière l'icône
            dl:add_circle_filled(V(gx + 6, icy), 8 * S, col(st.color, 0.28), 20)
        end
        draw_status_icon(st.key, gx + 6, icy, st.color)
        gx = gx + 15
        draw_text(label, gx, gy, 15, st.color, 1)
        gx = gx + text_w(label, 15) + 14
    end
    local head = header_height()
    hline(PAD, W - PAD, head - 4, C.line, 0.28)

    if #rows == 0 then return h end

    local top = math.max(1, rows[1].damage)
    local finish = fight_end(t)
    local y = head
    for _, r in ipairs(rows) do
        local c = row_color(r)
        local pct = 100 * r.damage / math.max(1, fight.total)
        local dps = r.damage / math.max(1, finish - r.first)
        -- pastille de couleur, nom, DPS (discret), dégâts, %
        dl:add_rect_filled(V(PAD, y + 5), V(PAD + 3, y + 23), col(c, 1), 0, 0)
        draw_text(r.name, PAD + 12, y + 3, 19, C.text, r.kind == "self" and 1 or 0.92)
        draw_text(fmt_pct(pct), W - PAD, y + 6, 15, C.muted, 1, 1)
        draw_text(fmt_int(r.damage), W - PAD - 58, y + 3, 19, C.text, 1, 1)
        draw_text(fmt_int(dps) .. "/s", W - PAD - 58 - 88, y + 6, 15, C.muted, 0.9, 1)
        -- barre : rail discret + remplissage à la couleur du joueur
        local bx1, bx2, by = PAD + 12, W - PAD, y + 27
        dl:add_rect_filled(V(bx1, by), V(bx2, by + 4), col(C.track, 0.07), 0, 0)
        -- même longueur qu'avant, découpée par type de dégâts : physique, élément, poison, nitro
        -- physique en gris, élément et altérations en couleur ; un petit espace sépare les parts
        local len = (bx2 - bx1) * r.damage / top
        local GAP = 2
        local parts = {}
        for _, seg in ipairs(bar_segments(r, c)) do
            local sw = len * seg[1] / math.max(1, r.damage)
            if sw > 0.5 then table.insert(parts, { sw, seg[2] }) end
        end
        local x = bx1
        for i, part in ipairs(parts) do
            local sw = part[1]
            local draw_w = (i < #parts) and math.max(0.5, sw - GAP) or sw
            dl:add_rect_filled(V(x, by), V(x + draw_w, by + 4), col(part[2], 0.9), 0, 0)
            x = x + sw
        end
        -- accumulation de chaque altération appliquée par ce joueur (valeur cumulée de ses coups)
        local sx = PAD + 12
        for _, item in ipairs(row_statuses(r)) do
            local label = fmt_int(item.value)
            draw_status_icon(item.st.key, sx + 5, y + ROW_H - 3 + text_h(13) / 2, item.st.color, 0.95)
            sx = sx + 13
            draw_text(label, sx, y + ROW_H - 3, 13, item.st.color, 0.95)
            sx = sx + text_w(label, 13) + 12
        end
        y = y + row_height(r)
    end
    return h
end

local dragging = false

--- Un menu du jeu est-il ouvert ? Vu en jeu (sonde, menu Start ouvert puis fermé) : GUIManager.get_IsHighHudInput
--- vaut true quand un menu prend la main (menu Start, carte…) et false en jeu. Le menu REFramework ouvert garde le
--- panneau visible (pour pouvoir le déplacer).
local function game_menu_open()
    if try(function() return reframework:is_drawing_ui() end) then return false end
    local gui = sdk.get_managed_singleton("app.GUIManager")
    if not gui then return false end
    return try(function() return gui:call("get_IsHighHudInput") end) == true
end
local draw_logged = nil
if cfg.demo then start_demo() end

-- ---------------------------------------------------------------- réglages dans le menu Options du jeu
-- Options > Paramètres de jeu > section « DPS meter » (voir options_menu.lua). Le menu du jeu fait foi ; ses valeurs
-- sont recopiées dans cfg (et le fichier de réglages). Si nos cases de sauvegarde sont vierges, on y met cfg.

local okm, options_menu = pcall(require, "wilds_dps_meter.options_menu")
if not okm then log.error("[Wilds DPS Meter] menu Options : " .. tostring(options_menu)); options_menu = nil end

local OPT = { show = 398, menus = 397, size = 396, background = 395, demo = 394 }
local SIZES = { 0.8, 1.0, 1.25, 1.5 }
local BACKGROUNDS = { 0.0, 0.3, 0.55, 0.8 }

local function nearest(list, v)
    local best, bi = math.huge, 1
    for i, x in ipairs(list) do
        if math.abs(x - v) < best then best, bi = math.abs(x - v), i end
    end
    return bi
end

local function option_texts(fr)
    if fr then
        return "DPS meter", "Réglages du DPS meter (dégâts et altérations en combat).", {
            { id = OPT.show, title = "Afficher le DPS meter", help = "Afficher le panneau des dégâts en combat.", choices = { "Oui", "Non" }, default = 0 },
            { id = OPT.menus, title = "Dans les menus du jeu", help = "Masquer le panneau quand un menu du jeu est ouvert (menu Start, carte…).", choices = { "Masquer", "Afficher" }, default = 0 },
            { id = OPT.size, title = "Taille", help = "Taille du panneau des dégâts.", choices = { "Petite", "Normale", "Grande", "Très grande" }, default = 1 },
            { id = OPT.background, title = "Fond", help = "Fond sombre derrière le panneau, pour la lisibilité.", choices = { "Aucun", "Léger", "Moyen", "Fort" }, default = 0 },
            { id = OPT.demo, title = "Mode démo", help = "Afficher des dégâts fictifs pour voir le panneau sans combat.", choices = { "Désactivé", "Activé" }, default = 0 },
        }
    end
    return "DPS meter", "DPS meter settings (damage and status effects in combat).", {
        { id = OPT.show, title = "Show DPS meter", help = "Show the damage panel in combat.", choices = { "Yes", "No" }, default = 0 },
        { id = OPT.menus, title = "In game menus", help = "Hide the panel while a game menu is open (Start menu, map…).", choices = { "Hide", "Show" }, default = 0 },
        { id = OPT.size, title = "Size", help = "Size of the damage panel.", choices = { "Small", "Normal", "Large", "Very large" }, default = 1 },
        { id = OPT.background, title = "Background", help = "Dark background behind the panel, for readability.", choices = { "None", "Light", "Medium", "Strong" }, default = 0 },
        { id = OPT.demo, title = "Demo mode", help = "Show fake damage to preview the panel without a fight.", choices = { "Off", "On" }, default = 0 },
    }
end

local options_installed, options_next, options_read_next = false, 0, 0
local options_fr, options_lang_next = nil, 0

local function sync_options(t)
    if not options_menu then return end
    if not options_installed then
        if t < options_next then return end
        options_next = t + 1
        options_fr = options_menu.is_french()
        local title, help, defs = option_texts(options_fr)
        local ok, res = pcall(options_menu.install, defs, title, help)
        if not ok then
            log.error("[Wilds DPS Meter] menu Options : " .. tostring(res))
            options_menu = nil
            return
        end
        if not res then return end -- données du jeu pas encore chargées
        if res == "unavailable" then options_menu = nil return end -- réglages dans le menu REFramework
        options_installed = true
        -- cases vierges : on y met les réglages actuels du meter
        local seed = {
            [OPT.show] = cfg.show and 0 or 1, [OPT.menus] = cfg.hide_menus and 0 or 1,
            [OPT.size] = nearest(SIZES, cfg.scale) - 1, [OPT.background] = nearest(BACKGROUNDS, cfg.opacity) - 1,
            [OPT.demo] = cfg.demo and 1 or 0,
        }
        for id, v in pairs(seed) do
            if not options_menu.is_saved(id) then options_menu.set(id, v) end
        end
    end
    -- langue du jeu : connue seulement une fois les options du joueur chargées, et modifiable en jeu
    if t >= options_lang_next then
        options_lang_next = t + 1
        local fr = options_menu.is_french()
        if fr ~= options_fr then
            options_fr = fr
            options_menu.relabel(select(3, option_texts(fr)), option_texts(fr))
        end
    end
    if t < options_read_next then return end
    options_read_next = t + 0.25
    local changed = false
    local function apply(key, value)
        if value ~= nil and cfg[key] ~= value then cfg[key] = value; changed = true end
    end
    local v = options_menu.get(OPT.show); if v then apply("show", v == 0) end
    v = options_menu.get(OPT.menus); if v then apply("hide_menus", v == 0) end
    v = options_menu.get(OPT.size); if v then apply("scale", SIZES[v + 1]) end
    v = options_menu.get(OPT.background); if v then apply("opacity", BACKGROUNDS[v + 1]) end
    v = options_menu.get(OPT.demo)
    if v then
        local want = v == 1
        if want ~= cfg.demo then
            cfg.demo = want; changed = true
            if want then start_demo() elseif fight and fight.demo then fight = nil end
        end
    end
    if changed then save_cfg() end
end

re.on_frame(function()
    frame_no = frame_no + 1
    local t = now()
    pcall(sync_options, t)
    tick_demo(t)
    pcall(check_deaths)
    local gok, gerr = pcall(update_gauges, t)
    if not gok and not gauge_error_logged then
        gauge_error_logged = true
        log.error("[Wilds DPS Meter] lecture des jauges : " .. tostring(gerr))
    end
    if not cfg.show then return end
    if cfg.hide_menus and game_menu_open() then return end

    local display = imgui.get_display_size()
    S = cfg.scale * math.max(0.6, display.y / 1080)
    local n = 0
    if fight then n = #fight.order end
    local w, h = W * S, layout_height() * S

    -- Le panneau est accroché par son bord bas (cfg.x, cfg.y = coin bas gauche) : il grandit vers le haut quand des
    -- joueurs s'ajoutent. Par défaut : en bas à droite, au-dessus de la barre d'objets.
    local ax, ay = cfg.x, cfg.y
    if ax < 0 or ay < 0 then ax, ay = display.x - (W + 40) * S, display.y * 0.85 end
    -- toujours dans l'écran (position enregistrée en pixels : la résolution a pu changer depuis)
    ax = math.max(0, math.min(ax, display.x - w - 8 * S))
    ay = math.max(h + 8 * S, math.min(ay, display.y))
    local want_x, want_y = ax, ay - h
    if not dragging then imgui.set_next_window_pos(Vector2f.new(want_x, want_y), 1) end
    imgui.set_next_window_size(Vector2f.new(w + 8 * S, h + 8 * S), 1)
    imgui.push_style_var(2, Vector2f.new(0, 0)) -- WindowPadding
    imgui.push_style_var(4, 0.0)                -- WindowBorderSize
    imgui.begin_window("##wilds_dps_meter", nil, WINDOW_FLAGS)
    local p = imgui.get_window_pos()
    ox, oy = p.x + 4 * S, p.y + 4 * S
    mirror = p.x + w / 2 > display.x / 2
    dl = imgui.get_window_draw_list()
    local ok, err = pcall(draw_panel, t)
    if not ok then dl:add_text(V(PAD, 8), col(SLOT_COLORS[1]), "Erreur : " .. tostring(err)) end
    -- une seule ligne dans re2_framework_log.txt : premier affichage réussi, ou première erreur
    if not draw_logged or (not ok and draw_logged ~= "error") then
        if ok then
            log.info(string.format("[Wilds DPS Meter] panneau affiché : %d ligne(s), échelle %.2f, écran %dx%d%s", n, S,
                math.floor(display.x), math.floor(display.y), fight and fight.demo and ", démo" or ""))
            draw_logged = "ok"
        else
            log.error("[Wilds DPS Meter] erreur d'affichage : " .. tostring(err))
            draw_logged = "error"
        end
    end
    imgui.end_window()
    imgui.pop_style_var(2)

    -- position déplacée à la souris (menu REFramework ouvert) : mémorisée (bord bas) une fois le bouton relâché
    if dragging or math.abs(p.x - want_x) > 0.5 or math.abs(p.y - want_y) > 0.5 then
        dragging = imgui.is_mouse_down(0)
        if not dragging then
            cfg.x, cfg.y = p.x, p.y + h
            save_cfg()
        end
    end
end)

-- ---------------------------------------------------------------- menu REFramework (touche Inser)

re.on_draw_ui(function()
    if not imgui.tree_node(TITLE .. " v" .. VERSION) then return end
    local changed
    if options_installed then
        imgui.text("Réglages : menu Options du jeu > Paramètres de jeu > DPS meter.")
    else
        -- menu Options indisponible (jeu modifié par une mise à jour…) : réglages ici
        changed, cfg.show = imgui.checkbox("Afficher le panneau", cfg.show)
        if changed then save_cfg() end
        changed, cfg.hide_menus = imgui.checkbox("Masquer dans les menus du jeu (menu Start, carte…)", cfg.hide_menus)
        if changed then save_cfg() end
        changed, cfg.scale = imgui.slider_float("Taille", cfg.scale, 0.6, 2.0, "%.2f")
        if changed then save_cfg() end
        changed, cfg.opacity = imgui.slider_float("Opacité du fond", cfg.opacity, 0.0, 1.0, "%.2f")
        if changed then save_cfg() end
        if fight and fight.demo then
            if imgui.button("Arrêter la démo") then fight = nil; cfg.demo = false; save_cfg() end
        elseif imgui.button("Démo") then
            start_demo(); cfg.demo = true; save_cfg()
        end
    end
    imgui.text("Menu ouvert : fais glisser le panneau pour le déplacer.")
    changed, cfg.idle = imgui.slider_int("Fin du combat après (s)", cfg.idle, 5, 120)
    if changed then save_cfg() end
    if imgui.button("Remettre à zéro") then fight = nil end
    imgui.same_line()
    if imgui.button("Position par défaut") then cfg.x, cfg.y = -1, -1; save_cfg() end

    changed, cfg.debug = imgui.checkbox("Journal des coups (diagnostic)", cfg.debug)
    if changed then save_cfg() end
    if cfg.debug then
        imgui.text(#debug_log .. " entrées")
        if imgui.button("Écrire le journal dans reframework\\data") then
            json.dump_file(DEBUG_FILE, debug_log)
        end
        imgui.same_line()
        if imgui.button("Vider") then debug_log = {} end
    end
    if not hook_ok then imgui.text_colored("Hook des dégâts introuvable.", 0xFF5555FF) end
    imgui.tree_pop()
end)
