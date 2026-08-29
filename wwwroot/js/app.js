// App State
let emails = [];
let selectedEmailId = null;
let currentFilter = 'all';
let hubConnection = null;

// DOM Elements
const connectionBadge = document.getElementById('connectionBadge');
const connectionText = document.getElementById('connectionText');
const emailListEl = document.getElementById('emailList');
const emptyListState = document.getElementById('emptyListState');
const detailPane = document.getElementById('detailPane');
const noEmailSelectedState = document.getElementById('noEmailSelectedState');
const emailDetailsContent = document.getElementById('emailDetailsContent');
const searchInput = document.getElementById('searchInput');

// Stats elements
const statTotal = document.getElementById('statTotal');
const statAttachments = document.getElementById('statAttachments');
const statPaperless = document.getElementById('statPaperless');
const statSmtpInfo = document.getElementById('statSmtpInfo');

// Console Drawer
const consoleDrawer = document.getElementById('consoleDrawer');
const consoleToggle = document.getElementById('consoleToggle');
const consoleLogs = document.getElementById('consoleLogs');

// Modals
const testEmailModal = document.getElementById('testEmailModal');
const infoModal = document.getElementById('infoModal');

// Initialize
document.addEventListener('DOMContentLoaded', async () => {
    setupSignalR();
    setupEventListeners();
    await loadServerInfo();
    await loadEmails();
});

// Setup SignalR Client
function setupSignalR() {
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl("/mailhub")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    hubConnection.on("EmailReceived", (emailSummary) => {
        logToConsole("Info", `📬 New email received: "${emailSummary.subject}" from ${emailSummary.from}`);
        
        // Check if already in list
        const existingIdx = emails.findIndex(e => e.id === emailSummary.id);
        if (existingIdx >= 0) {
            emails[existingIdx] = emailSummary;
        } else {
            emails.unshift(emailSummary);
        }

        updateStats();
        renderEmailList();

        // Highlight new entry
        const itemEl = document.querySelector(`[data-email-id="${emailSummary.id}"]`);
        if (itemEl) {
            itemEl.classList.add('new-arrival');
            setTimeout(() => itemEl.classList.remove('new-arrival'), 2000);
        }

        // If this is the first email, automatically select it
        if (!selectedEmailId) {
            selectEmail(emailSummary.id);
        }
    });

    hubConnection.on("EmailUpdated", (emailSummary) => {
        const idx = emails.findIndex(e => e.id === emailSummary.id);
        if (idx >= 0) {
            emails[idx] = emailSummary;
            updateStats();
            renderEmailList();

            if (selectedEmailId === emailSummary.id) {
                renderEmailDetail(emailSummary);
            }
        }
    });

    hubConnection.on("PaperlessStatusUpdated", (data) => {
        logToConsole(data.success ? "Info" : "Error", 
            `📄 Paperless: ${data.fileName} - ${data.message}`);
        
        const email = emails.find(e => e.id === data.emailId);
        if (email) {
            email.paperlessStatus = data.success ? "Uploaded" : "Failed";
            email.paperlessMessage = data.message;
            renderEmailList();
            if (selectedEmailId === email.id) {
                renderEmailDetail(email);
            }
        }
    });

    hubConnection.on("ServerLog", (log) => {
        logToConsole(log.level, log.message, log.timestamp);
    });

    hubConnection.onreconnecting(() => {
        setConnectionStatus(false, "Reconnecting...");
    });

    hubConnection.onreconnected(() => {
        setConnectionStatus(true, "Live (Connected)");
        loadEmails();
    });

    hubConnection.onclose(() => {
        setConnectionStatus(false, "Disconnected");
        setTimeout(startSignalR, 3000);
    });

    startSignalR();
}

async function startSignalR() {
    try {
        await hubConnection.start();
        setConnectionStatus(true, "Live (Connected)");
        logToConsole("Info", "Connected to Real-time Mail Stream.");
    } catch (err) {
        console.error("SignalR Connection Error: ", err);
        setConnectionStatus(false, "Connection Failed");
        setTimeout(startSignalR, 4000);
    }
}

function setConnectionStatus(isConnected, text) {
    if (isConnected) {
        connectionBadge.className = "status-badge connected";
    } else {
        connectionBadge.className = "status-badge disconnected";
    }
    connectionText.textContent = text;
}

// Fetch Initial Data
async function loadServerInfo() {
    try {
        const res = await fetch('/api/info');
        if (res.ok) {
            const data = await res.json();
            statSmtpInfo.textContent = `Port :${data.smtp.port} (${data.smtp.serverName})`;
            statPaperless.textContent = data.paperless.enabled 
                ? (data.paperless.hasToken ? "🟢 Configured" : "⚠️ Missing Token") 
                : "⚪ Standby";

            // Update Info Modal contents
            document.getElementById('infoSmtpPort').textContent = data.smtp.port;
            document.getElementById('infoSmtpDomain').textContent = data.smtp.serverName;
            document.getElementById('infoPaperlessUrl').textContent = data.paperless.baseUrl || 'None';
            document.getElementById('infoPaperlessStatus').textContent = data.paperless.enabled ? 'Enabled' : 'Disabled';
        }
    } catch (err) {
        console.error("Failed to load server info:", err);
    }
}

async function loadEmails() {
    try {
        const res = await fetch('/api/emails');
        if (res.ok) {
            emails = await res.json();
            updateStats();
            renderEmailList();

            if (emails.length > 0 && !selectedEmailId) {
                selectEmail(emails[0].id);
            }
        }
    } catch (err) {
        console.error("Failed to fetch emails:", err);
    }
}

function updateStats() {
    statTotal.textContent = emails.length;
    const totalAttachments = emails.reduce((acc, e) => acc + (e.attachments ? e.attachments.length : 0), 0);
    statAttachments.textContent = totalAttachments;
}

// Render Email List
function renderEmailList() {
    const searchTerm = searchInput.value.toLowerCase().trim();
    
    const filtered = emails.filter(email => {
        // Filter by pill
        if (currentFilter === 'attachments' && (!email.attachments || email.attachments.length === 0)) return false;
        if (currentFilter === 'uploaded' && email.paperlessStatus !== 'Uploaded') return false;
        if (currentFilter === 'failed' && email.paperlessStatus !== 'Failed' && email.paperlessStatus !== 'Error') return false;

        // Filter by search query
        if (searchTerm) {
            const matchFrom = (email.from || '').toLowerCase().includes(searchTerm);
            const matchTo = (email.to || []).some(t => t.toLowerCase().includes(searchTerm));
            const matchSubject = (email.subject || '').toLowerCase().includes(searchTerm);
            const matchBody = (email.textBody || '').toLowerCase().includes(searchTerm);
            const matchAttach = (email.attachments || []).some(a => a.fileName.toLowerCase().includes(searchTerm));
            return matchFrom || matchTo || matchSubject || matchBody || matchAttach;
        }

        return true;
    });

    emailListEl.innerHTML = '';

    if (filtered.length === 0) {
        emptyListState.style.display = 'flex';
        return;
    }

    emptyListState.style.display = 'none';

    filtered.forEach(email => {
        const li = document.createElement('li');
        li.className = `email-item ${email.id === selectedEmailId ? 'selected' : ''}`;
        li.setAttribute('data-email-id', email.id);

        const timeStr = formatRelativeTime(email.receivedAt);
        const attachCount = email.attachments ? email.attachments.length : 0;
        const recipient = (email.to && email.to.length > 0) ? email.to[0] : '';
        const paperlessClass = getPaperlessClass(email.paperlessStatus);

        li.innerHTML = `
            <div class="email-item-header">
                <span class="email-sender" title="${escapeHtml(email.from)}">${escapeHtml(email.from || 'Unknown Sender')}</span>
                <span class="email-time" title="${email.receivedAt}">${timeStr}</span>
            </div>
            <div class="email-subject">${escapeHtml(email.subject || '(No Subject)')}</div>
            <div class="email-snippet">${escapeHtml(email.textBody ? email.textBody.substring(0, 75) : '')}</div>
            <div class="email-item-footer">
                <span class="recipient-chip" title="To: ${escapeHtml(recipient)}">To: ${escapeHtml(recipient)}</span>
                <div class="meta-badges">
                    ${attachCount > 0 ? `<span class="attachment-badge">📎 ${attachCount}</span>` : ''}
                    <span class="paperless-pill ${paperlessClass}">${escapeHtml(email.paperlessStatus || '')}</span>
                </div>
            </div>
        `;

        li.addEventListener('click', () => selectEmail(email.id));
        emailListEl.appendChild(li);
    });
}

function getPaperlessClass(status) {
    if (!status) return 'none';
    const s = status.toLowerCase();
    if (s.includes('uploaded')) return 'uploaded';
    if (s.includes('fail') || s.includes('error')) return 'failed';
    if (s.includes('uploading')) return 'uploading';
    return 'ready';
}

// Select & Load Email Detail
async function selectEmail(emailId) {
    selectedEmailId = emailId;

    // Update active highlight in sidebar
    document.querySelectorAll('.email-item').forEach(el => {
        el.classList.toggle('selected', el.getAttribute('data-email-id') === emailId);
    });

    try {
        const res = await fetch(`/api/emails/${emailId}`);
        if (res.ok) {
            const email = await res.json();
            renderEmailDetail(email);
        }
    } catch (err) {
        console.error("Failed to fetch email detail:", err);
    }
}

function renderEmailDetail(email) {
    noEmailSelectedState.style.display = 'none';
    emailDetailsContent.style.display = 'block';

    document.getElementById('detailSubject').textContent = email.subject || '(No Subject)';
    document.getElementById('detailFrom').textContent = email.from || '(None)';
    document.getElementById('detailTo').textContent = (email.to || []).join(', ') || '(None)';
    document.getElementById('detailCc').textContent = (email.cc || []).join(', ') || 'None';
    document.getElementById('detailDate').textContent = new Date(email.receivedAt).toLocaleString();
    document.getElementById('detailSize').textContent = formatBytes(email.rawSize);

    // Paperless status in header
    const paperlessStatusEl = document.getElementById('detailPaperlessStatus');
    const paperlessMsgEl = document.getElementById('detailPaperlessMsg');
    const btnUploadAll = document.getElementById('btnUploadAllToPaperless');

    paperlessStatusEl.textContent = email.paperlessStatus || 'Not Processed';
    paperlessStatusEl.className = `paperless-pill ${getPaperlessClass(email.paperlessStatus)}`;
    paperlessMsgEl.textContent = email.paperlessMessage || '';

    if (email.attachments && email.attachments.length > 0) {
        btnUploadAll.style.display = 'inline-flex';
        btnUploadAll.onclick = () => uploadAllToPaperless(email.id);
    } else {
        btnUploadAll.style.display = 'none';
    }

    // Render HTML Tab
    const htmlFrame = document.getElementById('htmlBodyFrame');
    const noHtmlMsg = document.getElementById('noHtmlBodyMsg');
    if (email.htmlBody && email.htmlBody.trim()) {
        htmlFrame.style.display = 'block';
        noHtmlMsg.style.display = 'none';
        htmlFrame.srcdoc = `<!DOCTYPE html><html><head><style>body{font-family:sans-serif;padding:12px;color:#1e293b;}</style></head><body>${email.htmlBody}</body></html>`;
    } else {
        htmlFrame.style.display = 'none';
        noHtmlMsg.style.display = 'block';
    }

    // Render Plain Text Tab
    document.getElementById('textBodyPre').textContent = email.textBody || '(No plain text content)';

    // Render Attachments Tab
    const attachTabBadge = document.getElementById('tabAttachmentCount');
    const attachmentsGrid = document.getElementById('attachmentsGrid');
    const noAttachmentsMsg = document.getElementById('noAttachmentsMsg');

    const attachCount = email.attachments ? email.attachments.length : 0;
    attachTabBadge.textContent = attachCount;

    if (attachCount > 0) {
        attachmentsGrid.style.display = 'grid';
        noAttachmentsMsg.style.display = 'none';
        attachmentsGrid.innerHTML = '';

        email.attachments.forEach(att => {
            const card = document.createElement('div');
            card.className = 'attachment-card';
            const icon = getFileIcon(att.fileName, att.contentType);

            card.innerHTML = `
                <div class="attachment-card-header">
                    <div class="file-icon">${icon}</div>
                    <div class="file-info">
                        <div class="file-name" title="${escapeHtml(att.fileName)}">${escapeHtml(att.fileName)}</div>
                        <div class="file-meta">${att.formattedSize || formatBytes(att.size)} &bull; ${escapeHtml(att.contentType)}</div>
                    </div>
                </div>
                <div class="attachment-actions">
                    <a href="/api/emails/${email.id}/attachments/${att.id}" class="btn btn-sm btn-primary" download="${escapeHtml(att.fileName)}">
                        ⬇️ Download
                    </a>
                    <button class="btn btn-sm btn-accent" onclick="uploadAttachmentToPaperless('${email.id}', '${att.id}', this)">
                        📄 Send to Paperless
                    </button>
                </div>
            `;
            attachmentsGrid.appendChild(card);
        });
    } else {
        attachmentsGrid.style.display = 'none';
        noAttachmentsMsg.style.display = 'block';
    }

    // Render Headers Tab
    const headersTbody = document.getElementById('headersTableBody');
    headersTbody.innerHTML = '';
    if (email.headers) {
        Object.entries(email.headers).forEach(([k, v]) => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <th>${escapeHtml(k)}</th>
                <td>${escapeHtml(v)}</td>
            `;
            headersTbody.appendChild(tr);
        });
    }
}

// Actions: Upload to Paperless
async function uploadAttachmentToPaperless(emailId, attachmentId, buttonEl) {
    const originalText = buttonEl.innerHTML;
    buttonEl.disabled = true;
    buttonEl.innerHTML = '⏳ Uploading...';

    try {
        const res = await fetch(`/api/emails/${emailId}/attachments/${attachmentId}/paperless`, {
            method: 'POST'
        });
        const result = await res.json();
        
        if (result.success) {
            buttonEl.className = 'btn btn-sm btn-primary';
            buttonEl.innerHTML = '✅ Uploaded!';
        } else {
            buttonEl.className = 'btn btn-sm btn-danger';
            buttonEl.innerHTML = '❌ Failed';
            alert(`Paperless upload error:\n${result.message}`);
        }
    } catch (err) {
        buttonEl.innerHTML = '❌ Error';
        console.error("Paperless upload failed:", err);
    } finally {
        setTimeout(() => {
            buttonEl.disabled = false;
            buttonEl.innerHTML = originalText;
        }, 3000);
    }
}

async function uploadAllToPaperless(emailId) {
    const btn = document.getElementById('btnUploadAllToPaperless');
    btn.disabled = true;
    btn.innerHTML = '⏳ Uploading...';

    try {
        const res = await fetch(`/api/emails/${emailId}/paperless`, {
            method: 'POST'
        });
        const results = await res.json();
        const successCount = results.filter(r => r.success).length;
        alert(`Paperless upload finished.\n${successCount} of ${results.length} attachments uploaded successfully.`);
    } catch (err) {
        console.error("Upload all error:", err);
        alert("Upload request failed: " + err.message);
    } finally {
        btn.disabled = false;
        btn.innerHTML = '📄 Send All to Paperless';
    }
}

// Clear History
async function clearAllEmails() {
    if (!confirm("Are you sure you want to clear all received emails from memory?")) return;

    try {
        const res = await fetch('/api/emails', { method: 'DELETE' });
        if (res.ok) {
            emails = [];
            selectedEmailId = null;
            updateStats();
            renderEmailList();
            noEmailSelectedState.style.display = 'flex';
            emailDetailsContent.style.display = 'none';
            logToConsole("Info", "Email buffer cleared.");
        }
    } catch (err) {
        console.error("Failed to clear emails:", err);
    }
}

// Event Listeners
function setupEventListeners() {
    // Search input
    searchInput.addEventListener('input', () => renderEmailList());

    // Filter pills
    document.querySelectorAll('.filter-pill').forEach(pill => {
        pill.addEventListener('click', () => {
            document.querySelectorAll('.filter-pill').forEach(p => p.classList.remove('active'));
            pill.classList.add('active');
            currentFilter = pill.getAttribute('data-filter');
            renderEmailList();
        });
    });

    // Detail Tabs
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));

            btn.classList.add('active');
            const targetId = btn.getAttribute('data-target');
            const targetPane = document.getElementById(targetId);
            if (targetPane) targetPane.classList.add('active');
        });
    });

    // Console drawer toggle
    consoleToggle.addEventListener('click', () => {
        consoleDrawer.classList.toggle('expanded');
    });

    // Clear history button
    document.getElementById('btnClearHistory').addEventListener('click', clearAllEmails);

    // Test Email Modal
    document.getElementById('btnOpenTestModal').addEventListener('click', () => {
        testEmailModal.classList.add('open');
    });
    document.getElementById('closeTestModal').addEventListener('click', () => {
        testEmailModal.classList.remove('open');
    });
    document.getElementById('btnCancelTest').addEventListener('click', () => {
        testEmailModal.classList.remove('open');
    });

    // Send Test Email Form Submit
    document.getElementById('testEmailForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        const submitBtn = document.getElementById('btnSubmitTest');
        submitBtn.disabled = true;
        submitBtn.innerHTML = 'Sending...';

        const payload = {
            from: document.getElementById('testFrom').value,
            to: document.getElementById('testTo').value,
            subject: document.getElementById('testSubject').value,
            body: document.getElementById('testBody').value,
            includeSamplePdf: document.getElementById('testIncludePdf').checked
        };

        try {
            const res = await fetch('/api/emails/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                testEmailModal.classList.remove('open');
                logToConsole("Info", `Test email sent: "${payload.subject}"`);
            } else {
                alert("Failed to send test email.");
            }
        } catch (err) {
            console.error("Test email error:", err);
            alert("Error sending test email: " + err.message);
        } finally {
            submitBtn.disabled = false;
            submitBtn.innerHTML = 'Send Mock Email';
        }
    });

    // Info Modal
    document.getElementById('btnOpenInfoModal').addEventListener('click', () => {
        infoModal.classList.add('open');
    });
    document.getElementById('closeInfoModal').addEventListener('click', () => {
        infoModal.classList.remove('open');
    });
    document.getElementById('btnCloseInfo').addEventListener('click', () => {
        infoModal.classList.remove('open');
    });

    // Close modals on backdrop click
    window.addEventListener('click', (e) => {
        if (e.target === testEmailModal) testEmailModal.classList.remove('open');
        if (e.target === infoModal) infoModal.classList.remove('open');
    });
}

// Helpers
function logToConsole(level, message, timestamp) {
    const time = timestamp ? new Date(timestamp).toLocaleTimeString() : new Date().toLocaleTimeString();
    const row = document.createElement('div');
    row.className = `log-entry ${level.toLowerCase()}`;
    row.innerHTML = `<span class="log-time">[${time}]</span> <span class="log-msg">${escapeHtml(message)}</span>`;
    consoleLogs.appendChild(row);
    consoleLogs.scrollTop = consoleLogs.scrollHeight;
}

function formatRelativeTime(dateStr) {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    const now = new Date();
    const diffSecs = Math.floor((now - date) / 1000);

    if (diffSecs < 10) return 'just now';
    if (diffSecs < 60) return `${diffSecs}s ago`;
    const diffMins = Math.floor(diffSecs / 60);
    if (diffMins < 60) return `${diffMins}m ago`;
    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return `${diffHours}h ago`;
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

function formatBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function getFileIcon(fileName, contentType) {
    const lower = (fileName || '').toLowerCase();
    if (lower.endsWith('.pdf') || (contentType && contentType.includes('pdf'))) return '📄';
    if (lower.match(/\.(jpg|jpeg|png|gif|webp|bmp)$/)) return '🖼️';
    if (lower.match(/\.(docx?|odt|rtf|txt)$/)) return '📝';
    if (lower.match(/\.(xlsx?|ods|csv)$/)) return '📊';
    if (lower.match(/\.(zip|tar|gz|7z|rar)$/)) return '📦';
    return '📎';
}

function escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}
