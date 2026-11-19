-- Sonde de développement (pas publiée) : menu Options de Wilds, LECTURE SEULE.
-- * décrit des types (champs, signatures de méthodes) sans appeler aucune méthode ;
-- * lit les données d'options chargées (VariousDataManager._Setting._OptionData) par lecture de champs uniquement.
-- Écrit reframework\data\wilds_options_probe.json (à 20 s, puis toutes les 30 s).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end

local TYPES = {
    "app.user_data.OptionData", "app.user_data.OptionData.Data", "app.user_data.OptionData.Item",
    "app.VariousDataManager", "app.VariousDataManagerSetting", "app.user_data.VariousDataManagerSetting",
    "app.OptionManager", "app.OptionUtil", "app.Option", "app.OptionDef", "app.GUIOptionDef", "app.OptionParamDef",
    "app.cOption", "app.cOptionData", "app.cOptionManager", "app.savedata.cOptionParam", "app.savedata.cOption",
    "app.SaveDataManager", "app.savedata.cSystemSaveData", "app.savedata.cSystemData",
    "via.gui.message", "app.MessageManager", "app.helper.hMsg", "app.MessageUtil", "app.GUIMessageUtil",
    "app.GUIOptionBrightness", "app.GUI080000", "app.GUI080001", "app.GUI080004",
}

local out = { types = {}, singletons = {}, discovered = {}, option_data = {} }

local function type_name(td) return td and try(function() return td:get_full_name() end) or "?" end

local function describe(name)
    local td = sdk.find_type_definition(name)
    if not td then out.types[name] = false return nil end
    local t = { parent = type_name(td:get_parent_type()), fields = {}, methods = {} }
    for _, f in ipairs(td:get_fields() or {}) do
        local ft = try(function() return f:get_type():get_full_name() end) or "?"
        table.insert(t.fields, (f:is_static() and "static " or "") .. ft .. " " .. f:get_name())
        -- types rencontrés dans les champs : candidats à décrire ensuite
        if ft:match("^app%.") and ft:lower():find("option") then out.discovered[ft] = true end
    end
    for _, m in ipairs(td:get_methods() or {}) do
        local ps = {}
        for _, p in ipairs(try(function() return m:get_param_types() end) or {}) do table.insert(ps, type_name(p)) end
        local ret = try(function() return m:get_return_type():get_full_name() end) or "?"
        table.insert(t.methods, (try(m.is_static, m) and "static " or "") .. ret .. " " .. m:get_name() .. "(" .. table.concat(ps, ", ") .. ")")
        if ret:match("^app%.") and ret:lower():find("option") then out.discovered[ret] = true end
    end
    out.types[name] = t
    return td
end

local function field(obj, name) return obj and try(function() return obj:get_field(name) end) or nil end

local function read_option_data()
    local vdm = sdk.get_managed_singleton("app.VariousDataManager")
    out.option_data.vdm = vdm ~= nil
    local setting = field(vdm, "_Setting")
    out.option_data.setting_type = setting and type_name(setting:get_type_definition()) or nil
    local od = field(setting, "_OptionData")
    out.option_data.type = od and type_name(od:get_type_definition()) or nil
    if not od then return end
    local values = field(od, "_Values")
    out.option_data.values_type = values and type_name(values:get_type_definition()) or nil
    local n = values and try(function() return values:get_size() end) or 0
    out.option_data.count = n
    local entries, free_used = {}, {}
    for i = 0, n - 1 do
        local e = try(function() return values:get_element(i) end)
        if e then
            local id = field(e, "_OptionID")
            local items = field(e, "_Items")
            local row = {
                i = i, id = id, cat = field(e, "_Category"), type = field(e, "_OptionType"), parent = field(e, "_ParentOptionID"),
                scene = field(e, "_Scene"), def = field(e, "_DefaultValue"),
                items = items and try(function() return items:get_size() end) or nil,
            }
            if type(id) == "number" and id >= 380 then table.insert(free_used, id) end
            table.insert(entries, row)
        end
    end
    out.option_data.entries = entries
    out.option_data.high_ids_used = free_used
end

local singleton_names = { "app.VariousDataManager", "app.OptionManager", "app.SaveDataManager", "app.GUIManager",
    "app.MessageManager", "app.cOptionManager" }

local function run()
    for _, n in ipairs(TYPES) do describe(n) end
    -- deux passes sur les types découverts dans les champs et retours de méthodes
    for _ = 1, 2 do
        local list = {}
        for n in pairs(out.discovered) do if out.types[n] == nil then table.insert(list, n) end end
        for _, n in ipairs(list) do describe(n) end
    end
    for _, n in ipairs(singleton_names) do out.singletons[n] = sdk.get_managed_singleton(n) ~= nil end
    pcall(read_option_data)
    json.dump_file("wilds_options_probe.json", out)
end

local next_run = 20
re.on_frame(function()
    local t = os.clock()
    if t < next_run then return end
    next_run = t + 30
    local ok, err = pcall(run)
    if not ok then log.error("[Wilds Options Probe] " .. tostring(err)) end
end)
log.info("[Wilds Options Probe] chargée (lecture seule)")
