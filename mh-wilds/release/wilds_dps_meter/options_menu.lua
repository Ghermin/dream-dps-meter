-- Réglages du DPS meter dans le vrai menu Options du jeu (Options > Réglages du jeu > section « DPS meter »).
--
-- Principe (vu en jeu par sondes, en lecture seule) :
-- * Le menu Options est construit à partir de VariousDataManager._Setting._MenuSetting._OptionData :
--   Values[] (la liste affichée, dans l'ordre) et _DataTable[] (une case par identifiant interne, 400 cases).
-- * Une section = une entrée de type HEADLINE (4) ; un réglage = une entrée DEFAULT (0) avec une liste de choix
--   (_Items), rattachée à sa section par <ParentOptionID>. La valeur d'un réglage est l'index du choix.
-- * Les identifiants FREE_390 … FREE_399 sont réservés et inutilisés : même valeur interne et fixe, case vide dans
--   _DataTable, et case -1 dans la sauvegarde des options (cSystemSaveParam._SystemCommon._Option.Values, 400 cases,
--   index = identifiant fixe). Nos réglages y vivent sans rien ajouter à la sauvegarde.
-- * Les textes sont des GUID résolus par via.gui.message.get(System.Guid) : on y répond pour nos propres GUID.
--
-- Nos entrées sont des copies (MemberwiseClone) d'entrées existantes du jeu, dont on change l'identifiant, la section,
-- les textes et la liste de choix. Tout objet créé est retenu (add_ref_permanent) : sinon le ramasse-miettes du jeu
-- le libère et le jeu plante.
local M = {}

local function try(f, ...) local ok, r = pcall(f, ...) if ok then return r end end
local function field(o, n) return o and try(function() return o:get_field(n) end) or nil end

local CATEGORY_GAME_SETTINGS, TYPE_DEFAULT, TYPE_HEADLINE = 0, 0, 4
local HEADLINE_ID = 399

--- Écrit un élément dans un tableau du jeu.
local function set_elem(arr, i, v)
    if not pcall(function() arr[i] = v end) then arr:call("SetValue(System.Object, System.Int32)", v, i) end
end

--- Octet i d'un System.Byte[] (nombre ou objet valeur selon la version de REFramework).
local function byte_at(arr, i)
    local v = arr:get_element(i)
    if type(v) == "number" then return v end
    return v and (try(function() return v:get_field("m_value") end) or try(function() return v:get_field("mValue") end)) or 0
end

-- ---------------------------------------------------------------- textes (GUID inventés -> texte)


--- GUID d'un texte : calculé à partir du texte (FNV-1a), préfixe reconnaissable d75a0000-… Le même texte donne
--- toujours le même GUID : après un « Reset scripts », le script retrouve les textes des lignes déjà en place.
local function text_guid(str)
    local h = 0xcbf29ce484222325
    for i = 1, #str do h = ((h ~ str:byte(i)) * 0x100000001b3) & 0xFFFFFFFFFFFFFFFF end
    local s = string.format("d75a0000-7764-4d6d-8000-%012x", h & 0xFFFFFFFFFFFF)
    WILDS_DPS_TEXTS[s] = str
    return s
end

local guid_td = sdk.find_type_definition("System.Guid")
local guid_parse = guid_td and guid_td:get_method("Parse(System.String)")

--- Écrit un GUID (chaîne) dans le champ Guid `name` d'un objet : 16 octets écrits directement (REFramework refuse
--- d'affecter un champ Guid de cette façon).
local function set_guid(obj, name, s)
    local f = obj:get_type_definition():get_field(name)
    local t = obj:get_type_definition()
    while not f and t do t = t:get_parent_type(); f = t and t:get_field(name) end
    local g = guid_parse:call(nil, sdk.create_managed_string(s))
    local bytes = g:call("ToByteArray()")
    local off = f:get_offset_from_base()
    for i = 0, 15 do obj:write_byte(off + i, byte_at(bytes, i)) end
end

-- Le menu Options résout ses textes en natif (aucune fonction managée de traduction n'est appelée, vu par sonde) :
-- on le laisse remplir chaque élément texte, puis on écrit le nôtre. Les méthodes d'affichage du menu
-- (app.GUI030100.DispData pour une ligne, DispDataItem pour un choix du panneau de droite) reçoivent le
-- via.gui.Text à remplir ; après elles, l'élément porte le GUID demandé (get_MessageId) : si c'est un des nôtres,
-- set_Message(notre texte). La valeur affichée sur la ligne (setSettingText) passe par l'identifiant du réglage.
-- Globales : les hooks survivent à un rechargement du script, les tables de textes sont remplacées à chaque install.
WILDS_DPS_TEXTS = WILDS_DPS_TEXTS or {}   -- "guid" -> texte
WILDS_DPS_LABELS = WILDS_DPS_LABELS or {} -- identifiant -> { choices = { … } }

local function guid_key(g) return g and try(function() return g:call("ToString()") end) or nil end

local function write_text(text_el, s)
    try(function() text_el:call("set_Message(System.String)", sdk.create_managed_string(s)) end)
end

--- Après le jeu : si l'élément texte porte un de nos GUID, on y écrit notre texte.
local function fix_by_guid(text_el)
    local key = guid_key(try(function() return text_el:call("get_MessageId") end))
    local s = key and WILDS_DPS_TEXTS[key]
    if s then write_text(text_el, s); return true end
    return false
end

-- Un hook ne se retire pas : chaque méthode n'est hookée qu'une fois par session, et le hook appelle la fonction
-- rangée dans WILDS_DPS_AFTER, remplacée à chaque chargement du script (le code le plus récent s'applique).
WILDS_DPS_HOOKED = WILDS_DPS_HOOKED or {}
WILDS_DPS_AFTER = WILDS_DPS_AFTER or {}

local function install_text_hooks()
    local function hook(type_name, signature, after)
        local key = type_name .. "." .. signature
        WILDS_DPS_AFTER[key] = after
        if WILDS_DPS_HOOKED[key] then return end
        local td = sdk.find_type_definition(type_name)
        local m = td and td:get_method(signature)
        if not m then log.warn("[Wilds DPS Meter] menu Options : " .. key .. " introuvable") return end
        WILDS_DPS_HOOKED[key] = true
        sdk.hook(m, function(args)
            local s = thread.get_hook_storage()
            s.wdm_this, s.wdm_text = sdk.to_managed_object(args[2]), sdk.to_managed_object(args[4])
            s.wdm_value = args[5] and try(function() return sdk.to_int64(args[5]) & 0xFFFFFFFF end) or nil
            s.wdm_arg1 = args[3] and try(function() return sdk.to_int64(args[3]) & 0xFFFFFFFF end) or nil
        end, function(retval)
            local s = thread.get_hook_storage()
            if s.wdm_text and WILDS_DPS_AFTER[key] then pcall(WILDS_DPS_AFTER[key], s.wdm_this, s.wdm_text, s.wdm_value, s.wdm_arg1) end
            s.wdm_this, s.wdm_text = nil, nil
            return retval
        end)
    end
    -- lignes : titre et aide d'après l'identifiant du réglage (l'élément texte ne porte pas notre GUID)
    local function by_id(key)
        return function(this, text_el)
            local id = try(function() return this:call("get_OptionId") end)
            local labels = id and WILDS_DPS_LABELS[id]
            if labels and labels[key] then write_text(text_el, labels[key]) end
        end
    end
    hook("app.GUI030100.DispData", "setTitle(app.GUIBaseApp, via.gui.Text)", by_id("title"))
    hook("app.GUI030100.DispData", "updateTitle(app.GUIBaseApp, via.gui.Text)", by_id("title"))
    hook("app.GUI030100.DispData", "setTitle(app.GUIBaseApp, via.gui.Text, System.Guid)", by_id("title"))
    hook("app.GUI030100.DispData", "setHelp(app.GUIBaseApp, via.gui.Text)", by_id("help"))
    -- valeur affichée sur la ligne : libellé du choix courant de NOTRE réglage
    hook("app.GUI030100.DispData", "setSettingText(app.GUIBaseApp, via.gui.Text, System.Int32)", function(this, text_el, value)
        if fix_by_guid(text_el) then return end
        local id = try(function() return this:call("get_OptionId") end)
        local labels = id and WILDS_DPS_LABELS[id]
        if labels and labels.choices and value then
            local s = labels.choices[value + 1]
            if s then write_text(text_el, s) end
        end
    end)
    -- choix du panneau de droite : remplis par PartsDetail.updateItem(index, SelectItem, valeur) (vu par sonde) ;
    -- après lui, les textes du SelectItem portent le GUID du choix et de son aide : on écrit les nôtres.
    -- Chaque choix (SelectItem « Item_<index> ») contient txt_subIndex (libellé) et, sous PNL_FluentSize,
    -- txt_subSentence (description) ; le jeu les remplit en natif, sans GUID lisible : on écrit par identifiant.
    local text_type = sdk.typeof("via.gui.Text")
    local function child_text(ctrl, path)
        return try(function() return ctrl:call("getObject(System.String, System.Type)", path, text_type) end)
    end
    hook("app.GUI030100PartsDetail", "updateItem(System.Int32, via.gui.SelectItem, System.Int32)", function(this, select_item, _, index)
        local disp = try(function() return this:get_field("_DispData") end)
        local id = disp and try(function() return disp:call("get_OptionId") end)
        local labels = id and WILDS_DPS_LABELS[id]
        if not labels or not labels.choices then return end
        if not index then -- ancien relevé des paramètres : l'index est dans le nom (« Item_2 »)
            index = tonumber((try(function() return select_item:call("get_Name") end) or ""):match("(%d+)$"))
        end
        local label = index and labels.choices[index + 1]
        if not label then return end
        local title = child_text(select_item, "txt_subIndex")
        if title then write_text(title, label) end
        local desc = child_text(select_item, "PNL_FluentSize/txt_subSentence")
        if desc then write_text(desc, labels.choice_help and labels.choice_help[index + 1] or "") end
    end)
    -- (au cas où) méthodes d'un choix qui reçoivent directement le texte : le choix garde son GUID (_Title.MsgID, _Help)
    hook("app.GUI030100.DispDataItem", "setTitle(app.GUIBaseApp, via.gui.Text)", function(this, text_el)
        local info = try(function() return this:get_field("_Title") end)
        local key = guid_key(info and try(function() return info:call("get_MsgID") end))
        local str = key and WILDS_DPS_TEXTS[key]
        if str then write_text(text_el, str) end
    end)
    hook("app.GUI030100.DispDataItem", "setHelp(app.GUIBaseApp, via.gui.Text)", function(this, text_el)
        local key = guid_key(try(function() return this:get_field("_Help") end))
        local str = key and WILDS_DPS_TEXTS[key]
        if str then write_text(text_el, str) end
    end)
end

-- ---------------------------------------------------------------- construction des entrées

local function option_data()
    local vdm = sdk.get_managed_singleton("app.VariousDataManager")
    return field(field(field(vdm, "_Setting"), "_MenuSetting"), "_OptionData")
end

local function clone(obj)
    local c = obj:call("MemberwiseClone")
    if type(c) ~= "userdata" or not c.add_ref_permanent then c = sdk.to_managed_object(c) end
    c:add_ref_permanent()
    return c
end

local item_td = sdk.find_type_definition("app.user_data.OptionData.Item")

--- Liste de choix neuve (List<Item>) : copie de la liste du modèle, avec un tableau interne à nous.
local function make_items(template_list, labels)
    local list = clone(template_list)
    local arr = sdk.create_managed_array(item_td, #labels)
    arr:add_ref_permanent()
    local template_item = template_list:call("get_Item", 0)
    for i, label in ipairs(labels) do
        local it = clone(template_item)
        set_guid(it, "_MsgTitle", text_guid(label))
        set_guid(it, "_MsgHelp", text_guid(""))
        set_elem(arr, i - 1, it)
    end
    list:set_field("_items", arr)
    list:set_field("_size", #labels)
    return list
end

local function set_id(d, name, value)
    d:set_field("_" .. name, value)
    d:set_field("<" .. name .. ">k__BackingField", value)
end

--- Entrée du menu à partir d'un modèle du jeu.
local function make_entry(template, def)
    local d = clone(template)
    set_id(d, "OptionID", def.id)
    set_id(d, "ParentOptionID", def.parent or -1)
    set_id(d, "OptionType", def.headline and TYPE_HEADLINE or TYPE_DEFAULT)
    set_id(d, "Category", def.category or CATEGORY_GAME_SETTINGS)
    set_id(d, "Scene", 0) -- ALL : visible aussi depuis l'écran titre
    set_guid(d, "_MsgTitle", text_guid(def.title))
    set_guid(d, "_MsgHelp", text_guid(def.help or ""))
    d:set_field("_DefaultValue", def.default or 0)
    if def.choices then d:set_field("_Items", make_items(template:get_field("_Items"), def.choices)) end
    return d
end

--- Modèles : une section et un réglage à 2 choix de « Réglages du jeu », affichés en jeu.
local function find_templates(values, n)
    local headline, choice, last_game_settings = nil, nil, -1
    for i = 0, n - 1 do
        local e = values:get_element(i)
        if e and field(e, "<Category>k__BackingField") == CATEGORY_GAME_SETTINGS then
            last_game_settings = i
            local t = field(e, "<OptionType>k__BackingField")
            local items = field(e, "_Items")
            local count = items and try(function() return items:call("get_Count") end) or 0
            if t == TYPE_HEADLINE and not headline then headline = e end
            if t == TYPE_DEFAULT and count >= 2 and not choice and field(e, "<ParentOptionID>k__BackingField") ~= -1 then
                choice = e
            end
        end
    end
    return headline, choice, last_game_settings
end

-- ---------------------------------------------------------------- onglet à nous
-- Les onglets du menu sont des app.GUI030100.CategoryData { MessageId, Icon (app.IconDef.FACILITY), Category } rangés
-- dans _TitleCategoryDataTable / _InGameCategoryDataTable de l'instance du menu (recréée à chaque ouverture) ; les
-- listes d'onglets sont dimensionnées d'après ces tables (vu par sonde). On ajoute un onglet de catégorie 7
-- (« Interface » : connue du jeu, sans onglet ni réglage) avant la construction des onglets (guiAwake). La barre est
-- dessinée pour 8 icônes (113 px d'écart) : à 9, on les resserre sur la même largeur. L'en-tête de la liste
-- (txt_HeadingText) reçoit notre titre quand notre onglet est actif. Tout ce qui touche l'interface est fait dans
-- guiLateUpdate du menu, quand il est forcément en vie (jamais de référence gardée d'une image à l'autre).
local TAB_CATEGORY = 7
WILDS_DPS_TAB = WILDS_DPS_TAB or { icon = 83, title = "DPS meter" } -- 83 : icône de réglages sans onglet dans le menu

local gui_td = sdk.find_type_definition("app.GUI030100")
local cat_td = sdk.find_type_definition("app.GUI030100.CategoryData")
local tab_supported = gui_td ~= nil and cat_td ~= nil and gui_td:get_method("guiAwake()") ~= nil
    and gui_td:get_method("guiLateUpdate()") ~= nil

local function extend_tab_table(gui, name)
    local arr = gui:get_field(name)
    if not arr then return end
    local n = arr:get_size()
    for i = 0, n - 1 do
        if try(function() return arr:get_element(i):get_field("Category") end) == TAB_CATEGORY then return end
    end
    local new = sdk.create_managed_array(cat_td, n + 1)
    new:add_ref_permanent()
    for i = 0, n - 1 do new[i] = arr:get_element(i) end
    local vt = arr:get_element(0) -- copie de l'onglet « Paramètres de jeu »
    vt:set_field("Category", TAB_CATEGORY)
    vt:set_field("Icon", WILDS_DPS_TAB.icon)
    new[n] = vt
    gui:set_field(name, new)
end

local heading_path = "RootWindow/PNL_All/PNL_Group00/PNL_ref_Heading00/PNL_Heading/PNL_Text/txt_HeadingText"
local text_type = sdk.typeof("via.gui.Text")

--- Pendant la mise à jour du menu : onglets resserrés à 9, titre de notre onglet.
local function fix_tab_bar(gui)
    local list = gui:get_field("_CtrlTab"):get_field("_ScrList")
    local n = list:call("get_ItemCount")
    if n > 8 then
        local items = list:call("get_Items")
        local x0 = items:get_element(0):call("get_Position").x
        local step0 = items:get_element(1):call("get_Position").x - x0
        local xl = items:get_element(n - 1):call("get_Position").x
        -- seulement une fois TOUTES les icônes placées par le jeu à leur écart d'origine (113 px, retenu la première
        -- fois) : pendant la construction, elles sont à 0 ou à moitié placées ; une fois resserrées, l'écart a changé
        local placed = step0 > 50 and math.abs((xl - x0) - (n - 1) * step0) < 1
        if placed and not WILDS_DPS_TAB.base_step then WILDS_DPS_TAB.base_step = step0 end
        if placed and math.abs(step0 - WILDS_DPS_TAB.base_step) < 0.5 then
            local step = 7 * step0 / (n - 1) -- même largeur que 8 onglets
            for i = 0, n - 1 do
                local it = items:get_element(i)
                local p = it:call("get_Position")
                it:call("set_Position", Vector3f.new(x0 + i * step, p.y, p.z or 0))
            end
        end
    end
    local tabs = gui:call("getCategoryDataTable")
    local index = gui:get_field("_TabIndex")
    local cat = tabs and index and try(function() return tabs:get_element(index):get_field("Category") end)
    if cat == TAB_CATEGORY then
        local root = gui:get_field("_CtrlTab"):get_field("_RootCtrl")
        local heading = try(function() return root:call("getObject(System.String, System.Type)", heading_path, text_type) end)
        if heading and try(function() return heading:call("get_Message") end) ~= WILDS_DPS_TAB.title then
            write_text(heading, WILDS_DPS_TAB.title)
        end
    end
end

local function install_tab_hooks()
    if WILDS_DPS_TAB_HOOKS then return end
    WILDS_DPS_TAB_HOOKS = true
    sdk.hook(gui_td:get_method("guiAwake()"), function(args)
        local gui = sdk.to_managed_object(args[2])
        if gui then
            pcall(extend_tab_table, gui, "_TitleCategoryDataTable")
            pcall(extend_tab_table, gui, "_InGameCategoryDataTable")
        end
    end, function(r) return r end)
    sdk.hook(gui_td:get_method("guiLateUpdate()"), function(args)
        thread.get_hook_storage().wdm_gui = sdk.to_managed_object(args[2])
    end, function(r)
        local s = thread.get_hook_storage()
        local gui = s.wdm_gui
        s.wdm_gui = nil
        if gui and WILDS_DPS_FIX_TAB then pcall(WILDS_DPS_FIX_TAB, gui) end
        return r
    end)
end

--- Icône de l'onglet (app.IconDef.FACILITY) : prise en compte à la prochaine ouverture du menu.
function M.set_tab_icon(icon) WILDS_DPS_TAB.icon = icon end

--- Ajoute nos réglages : dans notre onglet si le jeu s'y prête, sinon en section de « Paramètres de jeu ».
--- defs = { { id, title, help, choices, default } … }. Renvoie true si c'est fait (ou déjà fait), false si les données
--- du jeu ne sont pas encore chargées, "unavailable" si nos identifiants sont pris par le jeu.
function M.install(defs, title, help)
    install_text_hooks()
    WILDS_DPS_TAB.title = title
    if tab_supported then
        WILDS_DPS_FIX_TAB = fix_tab_bar
        install_tab_hooks()
    end
    WILDS_DPS_LABELS[HEADLINE_ID] = { title = title, help = help }
    for _, d in ipairs(defs) do
        WILDS_DPS_LABELS[d.id] = { title = d.title, help = d.help, choices = d.choices }
        -- textes enregistrés d'avance (utile si les lignes sont déjà en place depuis un chargement précédent)
        text_guid(d.title); text_guid(d.help or "")
        for _, c in ipairs(d.choices or {}) do text_guid(c) end
    end
    text_guid(title); text_guid(help or ""); text_guid("")
    if WILDS_DPS_OPTIONS_INSTALLED then return true end
    local od = option_data()
    local values = field(od, "Values")
    local table_ = field(od, "_DataTable")
    if not values or not table_ then return false end
    local n = values:get_size()

    -- section déjà en place (script rechargé pendant la partie) : c'est la nôtre si son titre a notre préfixe de GUID
    local existing = table_:get_element(HEADLINE_ID)
    if existing ~= nil then
        local g = guid_key(field(existing, "_MsgTitle"))
        if g and g:sub(1, 8) == "d75a0000" then WILDS_DPS_OPTIONS_INSTALLED = true return true end
    end
    -- nos identifiants doivent être libres (un jour, une mise à jour du jeu pourrait les utiliser)
    for _, d in ipairs(defs) do
        if table_:get_element(d.id) ~= nil or existing ~= nil then
            log.warn("[Wilds DPS Meter] identifiants d'options utilisés par le jeu : réglages dans le menu REFramework")
            return "unavailable"
        end
    end

    local tpl_head, tpl_choice, insert_after = find_templates(values, n)
    if not tpl_head or not tpl_choice then log.warn("[Wilds DPS Meter] modèles d'options introuvables") return true end

    -- Onglet : réglages à plat dans la catégorie 7, la section reste comme repère, cachée (catégorie -1).
    -- Sans onglet : section « DPS meter » à la fin de Paramètres de jeu.
    local category = tab_supported and TAB_CATEGORY or CATEGORY_GAME_SETTINGS
    local entries = { make_entry(tpl_head, { id = HEADLINE_ID, headline = true, title = title, help = help,
        category = tab_supported and -1 or CATEGORY_GAME_SETTINGS }) }
    for _, d in ipairs(defs) do
        table.insert(entries, make_entry(tpl_choice, {
            id = d.id, parent = tab_supported and -1 or HEADLINE_ID, title = d.title, help = d.help, choices = d.choices,
            default = d.default, category = category,
        }))
    end

    -- nouvelle liste : nos entrées juste après la dernière de « Réglages du jeu »
    local data_td = sdk.find_type_definition("app.user_data.OptionData.Data")
    local arr = sdk.create_managed_array(data_td, n + #entries)
    arr:add_ref_permanent()
    local k = 0
    for i = 0, n - 1 do
        set_elem(arr, k, values:get_element(i)); k = k + 1
        if i == insert_after then
            for _, e in ipairs(entries) do set_elem(arr, k, e); k = k + 1 end
        end
    end
    for _, e in ipairs(entries) do set_elem(table_, field(e, "<OptionID>k__BackingField"), e) end
    od:set_field("Values", arr)
    WILDS_DPS_OPTIONS_INSTALLED = true
    log.info(string.format("[Wilds DPS Meter] menu Options : %s (%d réglages)", tab_supported and "onglet ajouté" or "section ajoutée à Paramètres de jeu", #defs))
    return true
end

-- ---------------------------------------------------------------- valeurs

local get_value = sdk.find_type_definition("app.OptionUtil"):get_method("getOptionValue(app.Option.ID)")
local set_value = sdk.find_type_definition("app.OptionUtil"):get_method("setOptionValue(app.Option.ID, System.Int32)")

--- Valeur actuelle (index du choix) d'un de nos réglages, ou nil si inconnue (case encore à -1 : jamais réglée).
function M.get(id)
    local v = try(function() return get_value:call(nil, id) end)
    if type(v) ~= "number" or v < 0 then return nil end
    return v
end

function M.set(id, v)
    pcall(function() set_value:call(nil, id, v) end)
end

--- La case de sauvegarde de ce réglage a-t-elle déjà une valeur ? (-1 = jamais réglé : le jeu renvoie alors la valeur
--- par défaut, qu'il ne faut pas prendre pour un choix du joueur.)
function M.is_saved(id)
    local sdm = sdk.get_managed_singleton("app.SaveDataManager")
    local sys = sdm and try(function() return sdm:call("get_SystemSaveData") end)
    local values = field(field(field(sys, "_SystemCommon"), "_Option"), "Values")
    local v = values and try(function() return values:get_element(id) end)
    if type(v) == "userdata" then v = try(function() return v:get_field("m_value") end) end
    return type(v) == "number" and v >= 0
end

--- Langue du texte du jeu : true si français (app.LanguageDef.LANGUAGE_APP.FRENCH = 2). Au démarrage, avant le
--- chargement des options du joueur, le jeu répond la langue par défaut : à relire régulièrement.
local get_lang = sdk.find_type_definition("app.OptionUtil"):get_method("getTextLanguage()")
function M.is_french()
    local lang = try(function() return get_lang:call(nil) end)
    if type(lang) == "userdata" then lang = try(function() return lang:get_field("value__") end) end
    return lang == 2
end

--- Change les textes (langue) sans toucher aux entrées déjà en place.
function M.relabel(defs, title, help)
    WILDS_DPS_LABELS[HEADLINE_ID] = { title = title, help = help }
    for _, d in ipairs(defs) do WILDS_DPS_LABELS[d.id] = { title = d.title, help = d.help, choices = d.choices } end
end

return M
