// Farklı bir API örneğini denemek için: http://localhost:5090/?api=http://localhost:5081
const API_BASE_URL = new URLSearchParams(location.search).get("api") || "http://localhost:5080";

// Test edilen bot üstteki listeden seçilir; adresle de verilebilir: http://localhost:5090/?bot=nexora-demo
// Öncelik: adresteki bot → bu tarayıcıda en son seçilen bot → listedeki ilk bot.
// Veritabanında hiç bot yoksa seçili bot olmaz (botKey null kalır).
let botKey = new URLSearchParams(location.search).get("bot") || readText("bot");
let bots = [];

// Bot listesi alınamadıysa nedeni; liste başarıyla alındıysa null.
let botListError = null;

const app = document.getElementById("app");
const messages = document.getElementById("messages");
const form = document.getElementById("chatForm");
const input = document.getElementById("messageInput");
const sendButton = document.getElementById("sendButton");
const statusElement = document.getElementById("status");
const statusDot = document.getElementById("statusDot");
const botNameElement = document.getElementById("botName");
const botAvatar = document.getElementById("botAvatar");
const debugToggle = document.getElementById("debugToggle");
const inspectorToggle = document.getElementById("inspectorToggle");
const inspectorClose = document.getElementById("inspectorClose");
const resetButton = document.getElementById("resetButton");
const botSelect = document.getElementById("botSelect");

const inspector = {
    empty: document.getElementById("inspectorEmpty"),
    body: document.getElementById("inspectorBody"),
    badge: document.getElementById("verdictBadge"),
    intent: document.getElementById("verdictIntent"),
    source: document.getElementById("factSource"),
    candidate: document.getElementById("factCandidate"),
    data: document.getElementById("factData"),
    scoreValue: document.getElementById("scoreValue"),
    scoreFill: document.getElementById("scoreFill"),
    marginValue: document.getElementById("marginValue"),
    marginFill: document.getElementById("marginFill"),
    raw: document.getElementById("rawJson"),
    session: document.getElementById("sessionValue"),
    model: document.getElementById("modelValue"),
    provider: document.getElementById("providerValue")
};

// Botun eşikleri /api/health'ten okunur (trainer kalibrasyonu); okunamazsa bu değerler gösterilir.
const DEFAULT_MIN_SCORE = 0.65;
const DEFAULT_MIN_MARGIN = 0.15;
let minScore = DEFAULT_MIN_SCORE;
let minMargin = DEFAULT_MIN_MARGIN;

const mobileQuery = window.matchMedia("(max-width: 960px)");

let sessionId = null;
let isSending = false;
let helpMenu = null;
let botName = "Chatbot";

// Bot art arda hızlı değiştirilirse yalnızca en son başlatılan yükleme ekrana yazılır.
let loadVersion = 0;

/* ---------- Tercihler (yalnızca bu tarayıcıda) ---------- */

function readText(key) {
    try {
        return localStorage.getItem(`chatbot-ui:${key}`);
    } catch {
        return null;
    }
}

function readPreference(key, fallback) {
    const value = readText(key);
    return value === null ? fallback : value === "true";
}

function writePreference(key, value) {
    try {
        localStorage.setItem(`chatbot-ui:${key}`, String(value));
    } catch {
        // Depolama kapalıysa tercih yalnızca bu oturumda geçerli olur.
    }
}

function applyDebug(enabled) {
    debugToggle.checked = enabled;
    app.classList.toggle("debug-off", !enabled);
}

function applyInspectorVisibility(visible) {
    if (mobileQuery.matches) {
        app.classList.toggle("inspector-open", visible);
    } else {
        app.classList.toggle("inspector-hidden", !visible);
    }
    inspectorToggle.setAttribute("aria-expanded", String(visible));
}

function isInspectorVisible() {
    return mobileQuery.matches
        ? app.classList.contains("inspector-open")
        : !app.classList.contains("inspector-hidden");
}

/* ---------- Mesaj çizimi ---------- */

function initials(name) {
    const letters = name
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map(word => word[0].toLocaleUpperCase("tr-TR"))
        .join("");
    return letters || "AC";
}

function formatTime(date = new Date()) {
    return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

function scrollToBottom() {
    // Menü kartları gibi yeni eklenen içerik yerleşimi tamamladıktan sonra kaydır.
    requestAnimationFrame(() => {
        messages.scrollTop = messages.scrollHeight;
    });
}

function createRow(role) {
    const row = document.createElement("div");
    row.className = `row ${role}`;

    if (role === "bot") {
        const avatar = document.createElement("div");
        avatar.className = "avatar small";
        avatar.setAttribute("aria-hidden", "true");
        avatar.textContent = initials(botName);
        row.appendChild(avatar);
    }

    const stack = document.createElement("div");
    stack.className = "stack";
    row.appendChild(stack);

    return { row, stack };
}

function addMessage(text, role, { action = null, chips = null, isError = false } = {}) {
    const { row, stack } = createRow(role);

    const bubble = document.createElement("div");
    bubble.className = isError ? "bubble error" : "bubble";
    bubble.textContent = text;

    if (action?.url) {
        const link = document.createElement("a");
        link.className = "action-link";
        link.href = action.url;
        link.textContent = `${action.text || "İlgili sayfayı aç"} →`;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        bubble.appendChild(document.createElement("br"));
        bubble.appendChild(link);
    }

    stack.appendChild(bubble);

    if (chips?.length) {
        const chipRow = document.createElement("div");
        chipRow.className = "chips";
        chips.forEach(chip => chipRow.appendChild(createChip(chip)));
        stack.appendChild(chipRow);
    }

    const time = document.createElement("span");
    time.className = "time";
    time.textContent = formatTime();
    stack.appendChild(time);

    messages.appendChild(row);
    scrollToBottom();
}

function createChip({ text, tone = null, mono = false }) {
    const chip = document.createElement("span");
    chip.className = ["chip", tone ? `tone-${tone}` : "", mono ? "mono" : ""].filter(Boolean).join(" ");
    chip.textContent = text;
    return chip;
}

function showTyping() {
    const { row, stack } = createRow("bot");
    row.classList.add("typing");
    row.setAttribute("aria-label", "Yazıyor");

    const bubble = document.createElement("div");
    bubble.className = "bubble";
    for (let i = 0; i < 3; i++) {
        const dot = document.createElement("span");
        dot.className = "dot";
        bubble.appendChild(dot);
    }

    stack.appendChild(bubble);
    messages.appendChild(row);
    scrollToBottom();
    return row;
}

/* ---------- Menüler ---------- */

function createMenu(titleText) {
    const menu = document.createElement("div");
    menu.className = "menu";

    const title = document.createElement("p");
    title.className = "menu-title";
    title.textContent = titleText;
    menu.appendChild(title);

    return menu;
}

function addCategoryMenu(menuData) {
    if (!menuData?.categories?.length) return;

    const menu = createMenu(menuData.prompt || "Yardımcı olabileceğim konular:");
    const tiles = document.createElement("div");
    tiles.className = "tiles";

    menuData.categories.forEach(category => {
        const tile = document.createElement("button");
        tile.type = "button";
        tile.className = "tile";

        const icon = document.createElement("span");
        icon.className = "tile-icon";
        icon.setAttribute("aria-hidden", "true");
        icon.textContent = category.icon || "•";

        const title = document.createElement("span");
        title.className = "tile-title";
        title.textContent = category.title;

        const count = document.createElement("span");
        count.className = "tile-count";
        count.textContent = `${category.items?.length ?? 0} seçenek`;

        tile.append(icon, title, count);
        tile.addEventListener("click", () => {
            addMessage(category.title, "user");
            addOptionMenu(category);
        });

        tiles.appendChild(tile);
    });

    menu.appendChild(tiles);
    messages.appendChild(menu);
    scrollToBottom();
}

function addOptionMenu(category, titleOverride = null) {
    if (!category?.items?.length) return;

    const menu = createMenu(titleOverride || category.prompt || "Bir seçenek seçin:");
    const options = document.createElement("div");
    options.className = "options";

    category.items.forEach(item => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "option";
        button.textContent = item.title;
        button.addEventListener("click", () => sendMessage(item.title, item.intent));
        options.appendChild(button);
    });

    const back = document.createElement("button");
    back.type = "button";
    back.className = "option back";
    back.textContent = "← Ana konular";
    back.addEventListener("click", () => addCategoryMenu(helpMenu));
    options.appendChild(back);

    menu.appendChild(options);
    messages.appendChild(menu);
    scrollToBottom();
}

/* ---------- Yanıtın yorumlanması ---------- */

// Yanıtın türü (result.type) → rozet metni ve rengi.
const TYPE_LABELS = {
    answer: { text: "Cevap", tone: "ok" },
    question: { text: "Bilgi bekleniyor", tone: "info" },
    options: { text: "Netleştirme", tone: "warn" },
    menu: { text: "Netleştirme", tone: "warn" },
    fallback: { text: "Fallback", tone: "danger" }
};

// Kararın verildiği adım (result.source) → açıklama. Sıra ChatbotService'teki karar zinciriyle aynı.
const SOURCE_LABELS = {
    "menu": "Menü seçimi",
    "exact-question": "Birebir menü sorusu",
    "alias": "Alias",
    "pending-input": "Bekleyen girdi",
    "model": "ML.NET",
    "follow-up": "Takip sorusu",
    "top-two": "ML (iki aday arasında) → seçenek",
    "category": "Kategori kelimesi → seçenek",
    "candidate-category": "ML (eşik altı) → adayın kategorisi",
    "fallback": "Fallback"
};

function describeVerdict(result) {
    return TYPE_LABELS[result.type] ?? TYPE_LABELS.answer;
}

function describeSource(result) {
    return SOURCE_LABELS[result.source] ?? result.source ?? "–";
}

function formatNumber(value) {
    return value == null ? "–" : Number(value).toFixed(3);
}

function buildChips(result) {
    const verdict = describeVerdict(result);
    const debug = result.debug ?? {};
    const chips = [
        { text: result.intent || verdict.text, tone: verdict.tone, mono: true },
        { text: describeSource(result) }
    ];

    if (debug.score != null) {
        chips.push({ text: `skor ${formatNumber(debug.score)}`, mono: true });
        chips.push({ text: `margin ${formatNumber(debug.margin)}`, mono: true });
    }

    // Cevap verilemediyse modelin neyi düşündüğünü göster.
    if (result.type !== "answer" && debug.candidateIntent) {
        chips.push({ text: `aday: ${debug.candidateIntent}`, mono: true });
        if (debug.secondIntent) {
            chips.push({ text: `2. aday: ${debug.secondIntent}`, mono: true });
        }
    }

    if (debug.usedExternalData) chips.push({ text: "canlı veri", tone: "info" });

    return chips;
}

function setMeter(fill, valueElement, value, threshold) {
    valueElement.textContent = formatNumber(value);

    if (value == null) {
        fill.style.width = "0";
        fill.className = "meter-fill";
        return;
    }

    const clamped = Math.max(0, Math.min(1, Number(value)));
    fill.style.width = `${(clamped * 100).toFixed(1)}%`;
    fill.className = `meter-fill ${clamped >= threshold ? "pass" : "fail"}`;
}

function updateInspector(result) {
    const verdict = describeVerdict(result);
    const debug = result.debug ?? {};

    inspector.empty.hidden = true;
    inspector.body.hidden = false;

    inspector.badge.textContent = verdict.text;
    inspector.badge.className = `badge tone-${verdict.tone}`;
    inspector.intent.textContent = result.intent || "-";
    inspector.source.textContent = describeSource(result);
    inspector.candidate.textContent = !debug.candidateIntent
        ? "–"
        : debug.secondIntent
            ? `${debug.candidateIntent} · 2. ${debug.secondIntent} (${formatNumber(debug.secondScore)})`
            : debug.candidateIntent;
    inspector.data.textContent = debug.usedExternalData
        ? "Harici veri sağlayıcı (canlı)"
        : result.type === "answer"
            ? "Bilgi tabanı (KnowledgeArticles)"
            : "–";

    setMeter(inspector.scoreFill, inspector.scoreValue, debug.score, minScore);
    setMeter(inspector.marginFill, inspector.marginValue, debug.margin, minMargin);

    inspector.raw.textContent = JSON.stringify(result, null, 2);
}

function applyThresholds(thresholds) {
    // Modeli olmayan bir bota geçilince önceki botun eşikleri ekranda kalmasın.
    minScore = thresholds?.minimumScore ?? DEFAULT_MIN_SCORE;
    minMargin = thresholds?.minimumMargin ?? DEFAULT_MIN_MARGIN;

    document.getElementById("scoreMark").style.left = `${minScore * 100}%`;
    document.getElementById("marginMark").style.left = `${minMargin * 100}%`;
    document.getElementById("thresholdHint").textContent =
        `Çizgiler bu botun doğrudan cevap eşikleri: skor ${minScore.toFixed(2)}, margin ${minMargin.toFixed(2)}.`
        + (thresholds ? " Tek kelimelik mesajlarda "
            + `${thresholds.singleWordMinimumScore.toFixed(2)} / ${thresholds.singleWordMinimumMargin.toFixed(2)}.` : "");
}

function resetInspector() {
    inspector.empty.hidden = false;
    inspector.body.hidden = true;
    inspector.session.textContent = "–";
}

/* ---------- API ---------- */

async function getErrorMessage(response) {
    const text = await response.text();
    if (!text) return `İstek başarısız oldu (HTTP ${response.status}).`;

    try {
        const json = JSON.parse(text);
        return json.error ?? text;
    } catch {
        return text;
    }
}

function setStatus(text, state) {
    statusElement.textContent = text;
    statusDot.className = `status-dot ${state ?? ""}`.trim();
}

function setComposerEnabled(enabled) {
    input.disabled = !enabled;
    sendButton.disabled = !enabled;
}

// API'deki bütün botları seçim listesine doldurur. Liste alınamazsa (API kapalı) listede
// yalnızca adresteki ya da bu tarayıcıda hatırlanan bot görünür.
async function loadBots() {
    try {
        const response = await fetch(`${API_BASE_URL}/api/bots`);
        if (response.ok) {
            bots = await response.json();
        } else {
            botListError = await getErrorMessage(response);
        }
    } catch {
        botListError = `Chatbot API'ye ulaşılamadı (${API_BASE_URL}). API çalışıyor mu?`;
    }

    // Hatırlanan bot artık yoksa (ya da hiç seçilmemişse) listedeki ilk bot açılır;
    // veritabanında hiç bot yoksa hiçbir bot seçilmez.
    if (botListError === null && !bots.some(bot => bot.publicKey === botKey)) {
        botKey = bots.length > 0 ? bots[0].publicKey : null;
    }

    const choices = bots.length > 0 || !botKey
        ? bots
        : [{ publicKey: botKey, name: botKey, modelReady: true }];

    if (choices.length === 0) {
        const empty = document.createElement("option");
        empty.value = "";
        empty.textContent = "Bot yok";
        botSelect.replaceChildren(empty);
    } else {
        botSelect.replaceChildren(...choices.map(bot => {
            const option = document.createElement("option");
            option.value = bot.publicKey;
            option.textContent = `${bot.name} (${bot.publicKey})${bot.modelReady ? "" : " · model yok"}`;
            return option;
        }));
    }

    botSelect.value = botKey ?? "";
    botSelect.disabled = bots.length < 2;
}

// Açılacak bot olmadığında nedenini gösterir: veritabanı boş ya da bot listesi alınamadı.
function showNoBot() {
    document.getElementById("botKey").textContent = "–";
    inspector.provider.textContent = "–";

    if (botListError) {
        setStatus("Bağlantı hatası", "error");
        addMessage(botListError, "bot", { isError: true });
        return;
    }

    setStatus("Bot yok");
    addMessage("Veritabanında henüz bot yok. Bir botun seed dosyasını ve eğitim verisini ekleyip modelini " +
        "Chatbot.Trainer ile eğitin, ardından API'yi yeniden başlatın. Örnek botlar samples/ klasöründe, " +
        "adımlar README'deki \"Yeni bir bot eklemek\" bölümünde.", "bot");
}

function switchBot(key) {
    botKey = key;
    writePreference("bot", key);

    // Sayfa yenilenince aynı bot açılsın; ?api= gibi diğer parametreler korunur.
    const params = new URLSearchParams(location.search);
    params.set("bot", key);
    history.replaceState(null, "", `${location.pathname}?${params}`);

    resetConversation();
}

async function initialize() {
    const version = ++loadVersion;
    const isStale = () => version !== loadVersion;

    setComposerEnabled(false);

    if (!botKey) {
        showNoBot();
        return;
    }

    document.getElementById("botKey").textContent = botKey;
    inspector.model.textContent = "–";
    inspector.provider.textContent = bots.find(bot => bot.publicKey === botKey)?.dataProvider
        ?? "yok (yalnızca bilgi tabanı)";
    setStatus("Bağlanıyor…");

    try {
        const healthResponse = await fetch(`${API_BASE_URL}/api/health?botKey=${encodeURIComponent(botKey)}`);
        if (isStale()) return;
        if (!healthResponse.ok) throw new Error(await getErrorMessage(healthResponse));

        const health = await healthResponse.json();
        if (isStale()) return;
        applyThresholds(health.thresholds);
        inspector.model.textContent = health.modelTrainedAtUtc
            ? new Date(health.modelTrainedAtUtc).toLocaleString("tr-TR")
            : "model yok";

        if (!health.modelReady) {
            setStatus("Model hazır değil", "error");
            addMessage(`'${botKey}' için model bulunamadı. Önce Chatbot.Trainer'ı bu bot için çalıştırın.`, "bot", { isError: true });
            setComposerEnabled(false);
            return;
        }

        const botResponse = await fetch(`${API_BASE_URL}/api/bots/${encodeURIComponent(botKey)}`);
        if (isStale()) return;
        if (!botResponse.ok) throw new Error(await getErrorMessage(botResponse));

        const bot = await botResponse.json();
        if (isStale()) return;
        helpMenu = bot.helpMenu;
        botName = bot.name || "Chatbot";

        botNameElement.textContent = botName;
        botAvatar.textContent = initials(botName);
        document.title = `${botName} · Test`;
        setStatus("Hazır", "ready");
        setComposerEnabled(true);

        addMessage(bot.welcomeMessage, "bot");
        addCategoryMenu(helpMenu);
        input.focus();
    } catch (error) {
        if (isStale()) return;
        setStatus("Bağlantı hatası", "error");
        const detail = error instanceof TypeError
            ? `Chatbot API'ye ulaşılamadı (${API_BASE_URL}). API çalışıyor mu?`
            : error.message;
        addMessage(detail, "bot", { isError: true });
    }
}

async function sendMessage(message, selectedIntent = null) {
    message = message.trim();
    if (!message || isSending) return;

    isSending = true;
    setComposerEnabled(false);
    // Cevap gelmeden bot değiştirilirse cevap yanlış botun sohbetine düşerdi.
    botSelect.disabled = true;
    addMessage(message, "user");
    input.value = "";

    const typing = showTyping();

    try {
        const response = await fetch(`${API_BASE_URL}/api/chat`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ botKey, sessionId, message, selectedIntent })
        });

        if (!response.ok) throw new Error(await getErrorMessage(response));

        const result = await response.json();
        sessionId = result.sessionId;
        inspector.session.textContent = sessionId;

        typing.remove();
        addMessage(result.message, "bot", { action: result.action, chips: buildChips(result) });
        updateInspector(result);

        if (result.type === "options") {
            addOptionMenu({ items: result.options }, "Seçenekler:");
        } else if (result.type === "menu") {
            addCategoryMenu(helpMenu);
        }
    } catch (error) {
        typing.remove();
        const detail = error instanceof TypeError
            ? "Chatbot API'ye ulaşılamadı. API çalışıyor mu?"
            : error.message;
        addMessage(detail, "bot", { isError: true });
    } finally {
        isSending = false;
        setComposerEnabled(true);
        botSelect.disabled = bots.length < 2;
        input.focus();
    }
}

function resetConversation() {
    if (isSending) return;
    sessionId = null;
    messages.replaceChildren();
    resetInspector();
    initialize();
}

/* ---------- Olaylar ---------- */

form.addEventListener("submit", event => {
    event.preventDefault();
    sendMessage(input.value);
});

// Enter ile gönderimi tarayıcının örtük form gönderimine bırakmıyoruz; karakter birleştirme (IME) sırasında göndermez.
input.addEventListener("keydown", event => {
    if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
        event.preventDefault();
        sendMessage(input.value);
    }
});

debugToggle.addEventListener("change", () => {
    applyDebug(debugToggle.checked);
    writePreference("debug", debugToggle.checked);
});

inspectorToggle.addEventListener("click", () => {
    const visible = !isInspectorVisible();
    applyInspectorVisibility(visible);
    if (!mobileQuery.matches) writePreference("inspector", visible);
});

inspectorClose.addEventListener("click", () => applyInspectorVisibility(false));

mobileQuery.addEventListener("change", () => {
    app.classList.remove("inspector-open", "inspector-hidden");
    applyInspectorVisibility(mobileQuery.matches ? false : readPreference("inspector", true));
});

resetButton.addEventListener("click", resetConversation);

botSelect.addEventListener("change", () => switchBot(botSelect.value));

applyDebug(readPreference("debug", true));
applyInspectorVisibility(mobileQuery.matches ? false : readPreference("inspector", true));
setComposerEnabled(false);
document.getElementById("apiValue").textContent = API_BASE_URL;
loadBots().then(initialize);
