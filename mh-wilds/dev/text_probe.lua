-- Module du chargeur (sonde) : quelles fonctions de texte voient passer nos GUID ? Hooks en LECTURE (ils comptent et
-- laissent le jeu faire). Écrit reframework\data\wilds_text_probe.json toutes les 2 s.
WDEV.text_counts = WDEV.text_counts or {}
local counts = WDEV.text_counts
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end

local function guid_of(arg)
    return try(function() return sdk.to_valuetype(arg, "System.Guid"):call("ToString()") end)
end

local function hook_guid_methods(type_name)
    local td = sdk.find_type_definition(type_name)
    if not td then return end
    for _, m in ipairs(td:get_methods()) do
        local params = try(function() return m:get_param_types() end) or {}
        local gi
        for i, p in ipairs(params) do if p:get_full_name() == "System.Guid" then gi = i; break end end
        if gi then
            local label = type_name .. "." .. m:get_name() .. "/" .. #params
            if not WDEV.hooks[label] then
                WDEV.hooks[label] = true
                local static = try(m.is_static, m)
                local argi = static and (gi + 1) or (gi + 2)
                pcall(sdk.hook, m, function(args)
                    local c = counts[label] or { calls = 0, ours = 0 }
                    counts[label] = c
                    c.calls = c.calls + 1
                    local g = guid_of(args[argi])
                    if g and g:sub(1, 8) == "d75a0000" then c.ours = c.ours + 1; c.last = g end
                end, function(r) return r end)
            end
        end
    end
end

for _, t in ipairs({ "via.gui.message", "app.MessageUtil", "via.gui.Text", "app.GUIUtil", "app.GUIManager" }) do
    hook_guid_methods(t)
end

local last = 0
return {
    run = function() end,
    frame = function()
        local t = os.clock()
        if t - last > 2 then last = t; json.dump_file("wilds_text_probe.json", counts) end
    end,
}
