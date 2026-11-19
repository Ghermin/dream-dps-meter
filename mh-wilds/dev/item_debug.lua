-- Module du chargeur (sonde) : remplace le traitement après PartsDetail.updateItem par un relevé (aucune écriture).
local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local out = { calls = 0, samples = {} }
local key = "app.GUI030100PartsDetail.updateItem(System.Int32, via.gui.SelectItem, System.Int32)"
local text_type, node_type = sdk.typeof("via.gui.Text"), sdk.typeof("via.gui.PlayObject")
local function walk(ctrl, depth, acc)
    if depth > 5 or not ctrl then return end
    table.insert(acc, string.rep(" ", depth) .. (try(function() return ctrl:call("get_Name") end) or "?") .. " <" ..
        (try(function() return ctrl:get_type_definition():get_full_name() end) or "?") .. ">" ..
        (function()
            local id = try(function() return ctrl:call("get_MessageId"):call("ToString()") end)
            local msg = try(function() return ctrl:call("get_Message") end)
            return id and (" id=" .. id .. " msg=" .. tostring(msg)) or ""
        end)())
    local kids = try(function() return ctrl:call("getChildren(System.Type)", node_type) end)
    for i = 0, (kids and kids:get_size() or 0) - 1 do walk(kids:get_element(i), depth + 1, acc) end
end
WILDS_DPS_AFTER[key] = function(this, sel)
    out.calls = out.calls + 1
    if #out.samples < 6 then
        local acc = {}
        walk(sel, 0, acc)
        local direct = try(function() return sel:call("getChildren(System.Type)", text_type) end)
        table.insert(out.samples, { tree = acc, direct_texts = direct and direct:get_size() or -1 })
    end
    json.dump_file("wilds_item_debug.json", out)
end
return { run = function() end }
