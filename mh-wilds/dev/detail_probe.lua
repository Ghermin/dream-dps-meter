-- Module du chargeur (sonde) : compte les appels des méthodes du panneau de détail du menu Options quand le curseur
-- passe sur nos lignes (hooks qui ne modifient rien). Écrit reframework\data\wilds_detail_probe.json.
WDEV.detail = WDEV.detail or {}
local counts = WDEV.detail
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
for _, t in ipairs({ "app.GUI030100PartsDetail", "app.GUI030100.DispDataItem" }) do
    local td = sdk.find_type_definition(t)
    for _, m in ipairs(td and td:get_methods() or {}) do
        local n = m:get_name()
        local label = t .. "." .. n .. "/" .. m:get_num_params()
        if n ~= ".ctor" and n ~= ".cctor" and not n:match("^get_") and not WDEV.hooks[label] then
            WDEV.hooks[label] = true
            pcall(sdk.hook, m, function() counts[label] = (counts[label] or 0) + 1 end, function(r) return r end)
        end
    end
end
local last = 0
return { run = function() end, frame = function()
    local t = os.clock()
    if t - last > 1 then last = t; json.dump_file("wilds_detail_probe.json", counts) end
end }
