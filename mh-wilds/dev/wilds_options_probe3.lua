-- Sonde de développement (pas publiée), v3, module du chargeur : enums d'identifiants d'options, sauvegarde des
-- options, textes. LECTURE SEULE : lectures de champs, getters get_…, Guid.ToString() et via.gui.message.get (lecture).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local function field(o, n) return o and try(function() return o:get_field(n) end) or nil end
local function tname(o) return o and try(function() return o:get_type_definition():get_full_name() end) or nil end
local function num(v)
    if type(v) ~= "userdata" then return v end
    return try(function() return v:get_field("m_value") end) or try(function() return v:get_field("mValue") end) or tostring(v)
end
local function guid_str(g) return g and try(function() return g:call("ToString()") end) or nil end
local msg_get = sdk.find_type_definition("via.gui.message"):get_method("get(System.Guid)")
local function text(g) return g and try(function() return msg_get:call(nil, g) end) or nil end

local function enum_fields(name)
    local res = {}
    local td = sdk.find_type_definition(name)
    for _, f in ipairs(td and td:get_fields() or {}) do
        if f:is_static() then res[f:get_name()] = num(try(f.get_data, f, nil)) end
    end
    return res
end

local function run()
    local out = {}
    -- correspondance noms -> identifiant interne / fixe, pour les FREE et les plus hauts
    local id, fixed = enum_fields("app.Option.ID"), enum_fields("app.Option.ID_Fixed")
    local map = {}
    for name, v in pairs(id) do
        if name:find("FREE") or (type(v) == "number" and v >= 380) then map[name] = { id = v, fixed = fixed[name] } end
    end
    out.free = map
    out.id_max, out.fixed_max = id.MAX, fixed.MAX

    -- sauvegarde système : getter, puis ses champs
    local sdm = sdk.get_managed_singleton("app.SaveDataManager")
    local sys = sdm and try(function() return sdm:call("get_SystemSaveData") end)
    out.sys_type = tname(sys)
    local fields = {}
    local td = sys and try(function() return sys:get_type_definition() end)
    for _, f in ipairs(td and td:get_fields() or {}) do
        if not f:is_static() then fields[f:get_name()] = try(function() return f:get_type():get_full_name() end) end
    end
    out.sys_fields = fields
    for fname, ft in pairs(fields) do
        if ft == "app.savedata.cOptionParam" then
            local vals = field(field(sys, fname), "Values")
            local n = vals and try(function() return vals:get_size() end) or 0
            out.save_values_field, out.save_values_len = fname, n
            local tail = {}
            for i = math.max(0, n - 14), n - 1 do tail[tostring(i)] = num(try(function() return vals:get_element(i) end)) end
            out.save_values_tail = tail
        end
    end

    -- textes de quelques entrées : titre, aide, premier choix
    local vdm = sdk.get_managed_singleton("app.VariousDataManager")
    local od = field(field(field(vdm, "_Setting"), "_MenuSetting"), "_OptionData")
    local values = field(od, "Values")
    local texts = {}
    for i = 0, 7 do
        local e = values and try(function() return values:get_element(i) end)
        if e then
            local items = field(e, "_Items")
            local it0 = items and try(function() return items:call("get_Item", 0) end)
            table.insert(texts, {
                id = field(e, "<OptionID>k__BackingField"), title_guid = guid_str(field(e, "_MsgTitle")),
                title = text(field(e, "_MsgTitle")), help = text(field(e, "_MsgHelp")),
                item0 = it0 and text(field(it0, "_MsgTitle")) or nil,
                items = items and try(function() return items:call("get_Count") end) or nil,
            })
        end
    end
    out.texts = texts
    json.dump_file("wilds_options_probe3.json", out)
end

return { run = function()
    local ok, err = pcall(run)
    if not ok then log.error("[Wilds Options Probe 3] " .. tostring(err)) end
end }
