const state = { workflows: [], status: "", selectedJobId: null };

const elements = {
  form: document.querySelector("#launch-form"),
  workflowType: document.querySelector("#workflow-type"),
  workflowDescription: document.querySelector("#workflow-description"),
  payload: document.querySelector("#payload"),
  simulateFailure: document.querySelector("#simulate-failure"),
  submit: document.querySelector("#submit-button"),
  jobsBody: document.querySelector("#jobs-body"),
  filters: document.querySelector("#status-filters"),
  drawer: document.querySelector("#detail-drawer"),
  backdrop: document.querySelector("#drawer-backdrop"),
  drawerTitle: document.querySelector("#drawer-title"),
  drawerSummary: document.querySelector("#drawer-summary"),
  drawerActions: document.querySelector("#drawer-actions"),
  timeline: document.querySelector("#event-timeline"),
  toast: document.querySelector("#toast")
};

const payloadTemplates = {
  "data-export": { dataset: "customer-analytics", format: "parquet", recordCount: 24000 },
  "document-processing": { documentName: "quarterly-report.pdf", classification: "financial" },
  "webhook-delivery": { destination: "https://example.com/hooks/orders", event: "order.completed" }
};

async function request(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options.headers || {}) }
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || problem.title || `Request failed (${response.status})`);
  }
  return response.status === 204 ? null : response.json();
}

async function checkHealth() {
  const pill = document.querySelector(".health-pill");
  const label = document.querySelector("#health-label");
  try {
    const response = await fetch("/health/ready");
    pill.classList.toggle("healthy", response.ok);
    pill.classList.toggle("unhealthy", !response.ok);
    label.textContent = response.ok ? "Platform healthy" : "Dependencies unavailable";
  } catch {
    pill.classList.add("unhealthy");
    label.textContent = "Control plane offline";
  }
}

async function loadWorkflows() {
  state.workflows = await request("/api/v1/workflows");
  elements.workflowType.innerHTML = state.workflows
    .map(workflow => `<option value="${escapeHtml(workflow.type)}">${escapeHtml(workflow.displayName)}</option>`)
    .join("");
  updateWorkflowTemplate();
}

function updateWorkflowTemplate() {
  const workflow = state.workflows.find(item => item.type === elements.workflowType.value);
  if (!workflow) return;
  elements.workflowDescription.textContent = `${workflow.description} · ${workflow.steps.length} durable steps`;
  elements.payload.value = JSON.stringify(payloadTemplates[workflow.type] || {}, null, 2);
}

async function loadStats() {
  const stats = await request("/api/v1/jobs/stats");
  document.querySelector("#stat-total").textContent = stats.total.toLocaleString();
  document.querySelector("#stat-active").textContent = (stats.queued + stats.running).toLocaleString();
  document.querySelector("#stat-succeeded").textContent = stats.succeeded.toLocaleString();
  document.querySelector("#stat-rate").textContent = `${stats.successRate.toFixed(1)}%`;
}

async function loadJobs() {
  const query = new URLSearchParams({ page: "1", pageSize: "50" });
  if (state.status) query.set("status", state.status);
  const page = await request(`/api/v1/jobs?${query}`);
  renderJobs(page.items);
  document.querySelector("#last-updated").textContent = `SYNCED ${new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
}

function renderJobs(jobs) {
  if (!jobs.length) {
    elements.jobsBody.innerHTML = '<tr><td colspan="6" class="empty">No jobs match this view. Launch one above to see the system move.</td></tr>';
    return;
  }

  elements.jobsBody.innerHTML = jobs.map(job => `
    <tr data-job-id="${job.id}">
      <td><span class="job-id">${job.id.slice(0, 8)}</span></td>
      <td>${escapeHtml(titleCase(job.workflowType))}</td>
      <td><span class="status ${job.status.toLowerCase()}">${escapeHtml(job.status)}</span></td>
      <td><span class="progress-track"><span style="width:${job.progressPercent}%"></span></span>${job.progressPercent}%</td>
      <td>${job.attemptCount} / ${job.maxAttempts}</td>
      <td>${relativeTime(job.createdAt)}</td>
    </tr>`).join("");

  elements.jobsBody.querySelectorAll("tr[data-job-id]").forEach(row =>
    row.addEventListener("click", () => openJob(row.dataset.jobId)));
}

async function submitJob(event) {
  event.preventDefault();
  let input;
  try {
    input = JSON.parse(elements.payload.value);
  } catch {
    showToast("Input payload must be valid JSON.");
    return;
  }

  if (elements.simulateFailure.checked) {
    input.simulateFailureAtStep = 2;
    input.failForAttempts = 1;
  }

  elements.submit.disabled = true;
  elements.submit.querySelector("span").textContent = "Submitting…";
  try {
    const job = await request("/api/v1/jobs", {
      method: "POST",
      headers: { "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify({ workflowType: elements.workflowType.value, input })
    });
    showToast(`Job ${job.id.slice(0, 8)} accepted for asynchronous processing.`);
    await Promise.all([loadStats(), loadJobs()]);
    await openJob(job.id);
  } catch (error) {
    showToast(error.message);
  } finally {
    elements.submit.disabled = false;
    elements.submit.querySelector("span").textContent = "Queue workflow";
  }
}

async function openJob(jobId) {
  state.selectedJobId = jobId;
  elements.drawer.classList.add("open");
  elements.backdrop.classList.add("open");
  elements.drawer.setAttribute("aria-hidden", "false");
  elements.drawerTitle.textContent = `Job ${jobId.slice(0, 8)}`;
  elements.drawerSummary.innerHTML = '<div class="empty">Loading execution state…</div>';

  try {
    const [job, events] = await Promise.all([
      request(`/api/v1/jobs/${jobId}`),
      request(`/api/v1/jobs/${jobId}/events`)
    ]);
    renderJobDetail(job, events);
  } catch (error) {
    elements.drawerSummary.innerHTML = `<div class="empty">${escapeHtml(error.message)}</div>`;
  }
}

function renderJobDetail(job, events) {
  elements.drawerTitle.textContent = titleCase(job.workflowType);
  elements.drawerSummary.innerHTML = `
    <div class="summary-grid">
      <div><span>Status</span><strong><i class="status ${job.status.toLowerCase()}">${escapeHtml(job.status)}</i></strong></div>
      <div><span>Progress</span><strong>${job.progressPercent}% · ${escapeHtml(job.currentStep)}</strong></div>
      <div><span>Job ID</span><strong>${job.id}</strong></div>
      <div><span>Correlation</span><strong>${escapeHtml(job.correlationId.slice(0, 16))}</strong></div>
      <div><span>Attempt</span><strong>${job.attemptCount} of ${job.maxAttempts}</strong></div>
      <div><span>Created</span><strong>${new Date(job.createdAt).toLocaleString()}</strong></div>
    </div>
    ${job.error ? `<p class="workflow-description"><strong>${escapeHtml(job.error.code)}</strong><br>${escapeHtml(job.error.message)}</p>` : ""}`;

  const actions = [];
  if (job.status === "Failed") actions.push('<button class="retry-button" data-action="retry">Retry failed job</button>');
  if (["Queued", "Running"].includes(job.status)) actions.push('<button class="cancel-button" data-action="cancel">Cancel job</button>');
  elements.drawerActions.innerHTML = actions.join("");
  elements.drawerActions.querySelectorAll("button").forEach(button =>
    button.addEventListener("click", () => mutateJob(job.id, button.dataset.action)));

  elements.timeline.innerHTML = events.length ? events.slice().reverse().map(item => `
    <article class="timeline-item">
      <strong>${escapeHtml(item.eventType.replaceAll(".", " / "))}</strong>
      <p>${escapeHtml(item.message)}</p>
      <time>${new Date(item.occurredAt).toLocaleString()} · ATTEMPT ${item.attempt}</time>
    </article>`).join("") : '<div class="empty">No events recorded.</div>';
}

async function mutateJob(jobId, action) {
  try {
    await request(`/api/v1/jobs/${jobId}/${action}`, { method: "POST" });
    showToast(action === "retry" ? "Job queued for another execution cycle." : "Cancellation requested.");
    await Promise.all([loadStats(), loadJobs(), openJob(jobId)]);
  } catch (error) {
    showToast(error.message);
  }
}

function closeDrawer() {
  state.selectedJobId = null;
  elements.drawer.classList.remove("open");
  elements.backdrop.classList.remove("open");
  elements.drawer.setAttribute("aria-hidden", "true");
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.classList.add("show");
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => elements.toast.classList.remove("show"), 3500);
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>'"]/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
  })[character]);
}

function titleCase(value) {
  return value.split("-").map(part => part.charAt(0).toUpperCase() + part.slice(1)).join(" ");
}

function relativeTime(value) {
  const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000);
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  if (Math.abs(seconds) < 60) return formatter.format(seconds, "second");
  const minutes = Math.round(seconds / 60);
  if (Math.abs(minutes) < 60) return formatter.format(minutes, "minute");
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) return formatter.format(hours, "hour");
  return formatter.format(Math.round(hours / 24), "day");
}

elements.form.addEventListener("submit", submitJob);
elements.workflowType.addEventListener("change", updateWorkflowTemplate);
document.querySelector("#refresh-button").addEventListener("click", () => Promise.all([loadStats(), loadJobs(), checkHealth()]));
document.querySelector("#drawer-close").addEventListener("click", closeDrawer);
elements.backdrop.addEventListener("click", closeDrawer);
elements.filters.addEventListener("click", event => {
  const button = event.target.closest("button[data-status]");
  if (!button) return;
  state.status = button.dataset.status;
  elements.filters.querySelectorAll("button").forEach(item => item.classList.toggle("active", item === button));
  loadJobs().catch(error => showToast(error.message));
});

async function refresh() {
  try {
    await Promise.all([loadStats(), loadJobs(), checkHealth()]);
    if (state.selectedJobId) await openJob(state.selectedJobId);
  } catch (error) {
    showToast(error.message);
  }
}

Promise.all([loadWorkflows(), refresh()]).catch(error => showToast(error.message));
window.setInterval(() => { if (!document.hidden) refresh(); }, 3000);
