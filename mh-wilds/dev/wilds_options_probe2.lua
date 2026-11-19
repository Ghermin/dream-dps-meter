-- Sonde de développement (pas publiée), v2 : où sont OptionData et les valeurs d'options sauvegardées. LECTURE SEULE :
-- lectures de champs, et seulement deux getters (Data.get_SaveValueIndex, Item : aucun appel).
-- Module du chargeur de développement : écrit reframework\data\wilds_options_probe2.json à chaque chargement.
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local function tname(o) return o and try(function() return o:get_type_definition():get_full_name() end) or nil end
local function field(o, n) return o and try(function() return o:get_field(n) end) or nil end
local function num(v)
    if type(v) ~= "userdata" then return v end
    return try(function() return v:get_field("m_value") end) or try(function() return v:get_field("mValue") end) or tostring(v)
end

local out = {}

--- Champs d'un objet dont le type contient « Option » (et leur type), sur un niveau.
local function option_fields(obj, label)
    local res = {}
    local td = obj and try(function() return obj:get_type_definition() end)
    for _, f in ipairs(td and td:get_fields() or {}) do
        local ft = try(function() return f:get_type():get_full_name() end) or "?"
        if not f:is_static() and (ft:find("Option") or f:get_name():find("Option")) then
            res[f:get_name()] = ft
        end
    end
    out[label] = res
    return res
end

local function run()
    local vdm = sdk.get_managed_singleton("app.VariousDataManager")
    local setting = field(vdm, "_Setting")
    local od
    for _, n in ipairs({ "_GameSystemSetting", "_MenuSetting", "_ItemSetting" }) do
        local sub = field(setting, n)
        local f = option_fields(sub, n)
        for fname, ft in pairs(f) do
            if ft == "app.user_data.OptionData" then od = field(sub, fname); out.option_data_path = "_Setting." .. n .. "." .. fname end
        end
    end
    if od then
        local values, table_ = field(od, "Values"), field(od, "_DataTable")
        out.values_len = values and try(function() return values:get_size() end)
        out.datatable_len = table_ and try(function() return table_:get_size() end)
        -- identifiants et index de sauvegarde : premières entrées, et toute entrée au-delà de 380
        local rows = {}
        for i = 0, (out.values_len or 0) - 1 do
            local e = try(function() return values:get_element(i) end)
            local id = field(e, "<OptionID>k__BackingField")
            if e and (i < 8 or (type(id) == "number" and id >= 380)) then
                local items = field(e, "_Items")
                local it0 = items and try(function() return items:call("get_Item", 0) end)
                table.insert(rows, {
                    i = i, id = id, type = field(e, "<OptionType>k__BackingField"), cat = field(e, "<Category>k__BackingField"),
                    parent = field(e, "<ParentOptionID>k__BackingField"),
                    save_index = try(function() return e:call("get_SaveValueIndex") end),
                    item0_type = tname(it0), item0_title = it0 and tostring(field(it0, "_MsgTitle")) or nil,
                    title = tostring(field(e, "_MsgTitle")),
                })
            end
        end
        out.rows = rows
        -- la table par identifiant : quelles cases sont vides ?
        local empty = {}
        for i = 0, (out.datatable_len or 0) - 1 do
            if try(function() return table_:get_element(i) end) == nil then table.insert(empty, i) end
        end
        out.datatable_empty = empty
    end
    -- sauvegarde : SaveDataManager._SystemSaveData._Data -> champ de type cOptionParam -> Values (Int16[])
    local sdm = sdk.get_managed_singleton("app.SaveDataManager")
    local sys = field(field(sdm, "_SystemSaveData"), "_Data")
    local f = option_fields(sys, "system_save_param")
    for fname, ft in pairs(f) do
        if ft == "app.savedata.cOptionParam" then
            local op = field(sys, fname)
            local vals = field(op, "Values")
            local n = vals and try(function() return vals:get_size() end) or 0
            out.save_values_len = n
            local tail = {}
            for i = math.max(0, n - 12), n - 1 do tail[tostring(i)] = num(try(function() return vals:get_element(i) end)) end
            out.save_values_tail = tail
        end
    end
    json.dump_file("wilds_options_probe2.json", out)
end

return { run = function()
    local ok, err = pcall(run)
    if not ok then log.error("[Wilds Options Probe 2] " .. tostring(err)) end
end }
