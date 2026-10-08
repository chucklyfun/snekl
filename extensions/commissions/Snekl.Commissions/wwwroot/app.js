(() => {
  const state = {
    token: null,
    user: null,
    contentBaseUrl: null,
    view: 'listings',
    listings: [],
    orders: [],
    studio: null,
    selectedOrder: null,
  };

  const $ = (id) => document.getElementById(id);
  const viewEl = $('view');
  const errorEl = $('error');
  const whoEl = $('who');

  function showError(msg) {
    errorEl.hidden = !msg;
    errorEl.textContent = msg || '';
  }

  function escape(s) {
    return String(s ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;');
  }

  function money(cents, currency) {
    if (cents == null) return 'quote';
    return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency || 'USD' }).format(cents / 100);
  }

  async function api(path, opts = {}) {
    const headers = { ...(opts.headers || {}) };
    if (opts.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
    if (state.token) headers.Authorization = `Bearer ${state.token}`;
    const res = await fetch(path, { ...opts, headers });
    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = text; }
    if (!res.ok) throw new Error((data && data.error) || text || res.statusText);
    return data;
  }

  function field(label, control) {
    return `<label>${escape(label)}${control}</label>`;
  }

  async function refreshStudio() {
    state.studio = await api('/api/studio');
  }

  async function refreshListings() {
    if (!state.contentBaseUrl) return;
    state.listings = await api(`/api/listings?content=${encodeURIComponent(state.contentBaseUrl)}`);
  }

  async function refreshOrders() {
    state.orders = await api('/api/orders');
  }

  function renderTabs() {
    document.querySelectorAll('#tabs button').forEach((btn) => {
      btn.classList.toggle('active', btn.dataset.view === state.view);
    });
  }

  function renderListings() {
    const cards = state.listings.map((listing) => `
      <article class="panel">
        <h3>${escape(listing.title)} ${listing.adult ? '<span class="pill adult">Adult</span>' : ''}</h3>
        <p class="muted">${escape(listing.artistUsername)} · ${escape(listing.pricingMode)} · ${money(listing.priceCents, listing.currency)} · ${listing.openSlots} slots</p>
        <p>${escape(listing.description)}</p>
        <div class="row">
          ${field('Brief', '<textarea id="brief-' + listing.id + '"></textarea>')}
          ${field('Payment', `<select id="pay-${listing.id}">
            <option value="off_platform">Off-platform</option>
            ${listing.adult ? '<option value="ccbill">CCBill milestones</option>' : '<option value="escrow">Escrow record</option>'}
          </select>`)}
        </div>
        <button class="primary" data-request="${listing.id}">Request</button>
      </article>`).join('') || '<p class="muted">No open listings.</p>';

    viewEl.innerHTML = `
      <div class="stack">
        <article class="panel">
          <h2>New listing</h2>
          <div class="row">
            ${field('Title', '<input id="listing-title" />')}
            ${field('Pricing', '<select id="listing-mode"><option value="fixed">Fixed</option><option value="quote">Quote</option></select>')}
            ${field('Price (cents)', '<input id="listing-price" type="number" min="1" />')}
            ${field('Slots', '<input id="listing-slots" type="number" min="0" value="3" />')}
          </div>
          ${field('Description', '<textarea id="listing-desc"></textarea>')}
          <label><input id="listing-adult" type="checkbox" /> Adult / NSFW (CCBill or off-platform only; never Stripe)</label>
          <button class="primary" id="create-listing">Create listing</button>
        </article>
        ${cards}
      </div>`;

    $('create-listing').onclick = async () => {
      try {
        showError('');
        await api('/api/listings', {
          method: 'POST',
          body: JSON.stringify({
            title: $('listing-title').value,
            description: $('listing-desc').value,
            pricingMode: $('listing-mode').value,
            priceCents: Number($('listing-price').value || 0) || null,
            openSlots: Number($('listing-slots').value || 0),
            adult: $('listing-adult').checked,
          }),
        });
        await refreshListings();
        render();
      } catch (err) { showError(err.message); }
    };

    viewEl.querySelectorAll('[data-request]').forEach((btn) => {
      btn.onclick = async () => {
        const id = btn.getAttribute('data-request');
        try {
          showError('');
          await api(`/api/listings/${id}/requests`, {
            method: 'POST',
            body: JSON.stringify({
              brief: $(`brief-${id}`).value,
              paymentMethod: $(`pay-${id}`).value,
            }),
          });
          state.view = 'orders';
          await refreshOrders();
          render();
        } catch (err) { showError(err.message); }
      };
    });
  }

  function milestoneEditor(order) {
    const canAccept = (order.actions || []).includes('accepted');
    if (order.paymentMethod === 'ccbill' && canAccept) {
      return `
      <div class="stack" id="milestone-editor">
        <h3>Milestones (must sum to price; leave empty for one full payment)</h3>
        <div id="milestone-rows"></div>
        <button type="button" class="ghost" id="add-milestone">Add milestone</button>
      </div>`;
    }
    if (!order.milestones?.length) return '';
    return `<div class="stack"><h3>Milestones</h3>${order.milestones.map((m) =>
      `<div class="row"><span>${escape(m.sequence)}. ${escape(m.label)}</span><span>${money(m.amountCents, order.currency)}</span><span class="pill">${escape(m.status)}</span></div>`
    ).join('')}</div>`;
  }

  function readMilestones() {
    const rows = [...document.querySelectorAll('#milestone-rows .milestone-row')];
    if (!rows.length) return null;
    return rows.map((row) => ({
      label: row.querySelector('.m-label').value,
      amountCents: Number(row.querySelector('.m-amount').value || 0),
    }));
  }

  function bindMilestoneEditor() {
    const host = $('milestone-rows');
    if (!host) return;
    const add = () => {
      const row = document.createElement('div');
      row.className = 'milestone-row';
      row.innerHTML = `
        <label>Label<input class="m-label" /></label>
        <label>Cents<input class="m-amount" type="number" min="1" /></label>
        <button type="button" class="ghost">Remove</button>`;
      row.querySelector('button').onclick = () => row.remove();
      host.appendChild(row);
    };
    $('add-milestone').onclick = add;
  }

  function renderOrders() {
    if (state.selectedOrder) {
      const order = state.selectedOrder;
      const labels = {
        accepted: 'Accept', quoted: 'Quote', in_progress: 'Start', delivered: 'Mark delivered',
        complete: 'Complete', completed: 'Complete', hold: 'Mark escrow held', release: 'Mark escrow released',
        declined: 'Decline', cancelled: 'Cancel', pay_milestone: 'Pay next milestone',
      };
      viewEl.innerHTML = `
        <article class="panel stack">
          <button class="ghost" id="back-orders">← Orders</button>
          <h2>${escape(order.listingTitle)}</h2>
          <p class="muted">${escape(order.artistUsername)} ← ${escape(order.clientUsername)} · ${escape(order.status.replaceAll('_', ' '))} · ${money(order.priceCents, order.currency)} · ${escape(order.paymentMethod)} / escrow ${escape(order.escrowState)}</p>
          <p>${escape(order.brief)}</p>
          ${milestoneEditor(order)}
          <div class="row" id="order-actions"></div>
          ${order.status === 'requested' && order.pricingMode === 'quote' ? field('Quote cents', '<input id="quote-cents" type="number" min="1" />') + '<button class="primary" id="send-quote">Send quote</button>' : ''}
          <ul class="timeline">${order.timeline.map((t) => `<li><strong>${escape(t.actorUsername)}</strong> · ${escape(t.kind)} · ${escape(t.body)}</li>`).join('')}</ul>
        </article>`;
      $('back-orders').onclick = () => { state.selectedOrder = null; render(); };
      bindMilestoneEditor();
      const actions = $('order-actions');
      (order.actions || []).forEach((action) => {
        const btn = document.createElement('button');
        btn.className = action === 'pay_milestone' ? 'primary' : 'ghost';
        btn.textContent = labels[action] || action;
        btn.onclick = async () => {
          try {
            showError('');
            if (action === 'pay_milestone') {
              if (!order.nextMilestoneId) throw new Error('No open milestone.');
              const checkout = await api(`/api/orders/${order.id}/milestones/${order.nextMilestoneId}/checkout`, { method: 'POST' });
              window.open(checkout.url, '_blank', 'noopener');
              return;
            }
            if (action === 'hold' || action === 'release') {
              const map = { hold: 'held', release: 'released' };
              state.selectedOrder = await api(`/api/orders/${order.id}/escrow`, { method: 'POST', body: JSON.stringify({ state: map[action] }) });
            } else {
              const body = { status: action === 'complete' ? 'completed' : action };
              if (action === 'accepted') body.milestones = readMilestones();
              state.selectedOrder = await api(`/api/orders/${order.id}/transition`, { method: 'POST', body: JSON.stringify(body) });
            }
            await refreshOrders();
            render();
          } catch (err) { showError(err.message); }
        };
        actions.appendChild(btn);
      });
      const quoteBtn = $('send-quote');
      if (quoteBtn) {
        quoteBtn.onclick = async () => {
          try {
            showError('');
            state.selectedOrder = await api(`/api/orders/${order.id}/quote`, {
              method: 'POST',
              body: JSON.stringify({ priceCents: Number($('quote-cents').value || 0) }),
            });
            await refreshOrders();
            render();
          } catch (err) { showError(err.message); }
        };
      }
      return;
    }

    viewEl.innerHTML = `<div class="stack">${state.orders.map((order) => `
      <article class="panel">
        <h3>${escape(order.listingTitle)}</h3>
        <p class="muted">${escape(order.status)} · ${money(order.priceCents, order.currency)} · ${escape(order.paymentMethod)}</p>
        <button class="ghost" data-open="${order.id}">Open</button>
      </article>`).join('') || '<p class="muted">No orders yet.</p>'}</div>`;
    viewEl.querySelectorAll('[data-open]').forEach((btn) => {
      btn.onclick = async () => {
        try {
          state.selectedOrder = await api(`/api/orders/${btn.getAttribute('data-open')}`);
          render();
        } catch (err) { showError(err.message); }
      };
    });
  }

  function renderStudio() {
    const s = state.studio || {};
    viewEl.innerHTML = `
      <article class="panel stack">
        <h2>Studio</h2>
        ${field('Display name', `<input id="studio-name" value="${escape(s.displayName || '')}" />`)}
        ${field('Terms', `<textarea id="studio-terms">${escape(s.termsOfService || '')}</textarea>`)}
        ${field('Portfolio note', `<textarea id="studio-note">${escape(s.portfolioNote || '')}</textarea>`)}
        <label><input id="studio-open" type="checkbox" ${s.acceptingCommissions ? 'checked' : ''}/> Accepting commissions</label>
        <h3>CCBill (adult milestones)</h3>
        <p class="muted">Charges settle to your CCBill merchant account. Paste this webhook URL into your CCBill admin:</p>
        <p><code>${escape(s.ccBillWebhookUrl || '')}</code></p>
        <div class="row">
          ${field('Client account', `<input id="cc-acc" value="${escape(s.ccBillClientAcc || '')}" autocomplete="off" />`)}
          ${field('Subaccount', `<input id="cc-sub" value="${escape(s.ccBillSubacc || '')}" autocomplete="off" />`)}
          ${field('FlexForms id', `<input id="cc-flex" value="${escape(s.ccBillFlexFormId || '')}" autocomplete="off" />`)}
        </div>
        ${field('Dynamic pricing salt (write-only; leave blank to keep)', `<input id="cc-salt" type="password" autocomplete="new-password" placeholder="${s.ccBillSaltSet ? 'Salt saved' : 'Required for checkout'}" />`)}
        ${field('Affiliate id (optional, Snekl)', `<input id="cc-aff" value="${escape(s.ccBillAffiliateId || '')}" autocomplete="off" />`)}
        <p class="muted">${s.ccBillReady ? 'CCBill is ready for adult listings.' : 'CCBill is incomplete until account, subaccount, FlexForms id, and salt are set.'}</p>
        <button class="primary" id="save-studio">Save studio</button>
      </article>`;
    $('save-studio').onclick = async () => {
      try {
        showError('');
        const body = {
          displayName: $('studio-name').value,
          termsOfService: $('studio-terms').value,
          portfolioNote: $('studio-note').value,
          acceptingCommissions: $('studio-open').checked,
          ccBillClientAcc: $('cc-acc').value,
          ccBillSubacc: $('cc-sub').value,
          ccBillFlexFormId: $('cc-flex').value,
          ccBillAffiliateId: $('cc-aff').value,
        };
        const salt = $('cc-salt').value;
        if (salt) body.ccBillSalt = salt;
        state.studio = await api('/api/studio', { method: 'PUT', body: JSON.stringify(body) });
        render();
      } catch (err) { showError(err.message); }
    };
  }

  function render() {
    renderTabs();
    whoEl.textContent = state.user ? `${state.user.displayName || state.user.username}` : 'Not signed in';
    if (!state.token) {
      viewEl.innerHTML = '<p class="muted">Open this extension from Snekl to sign in.</p>';
      return;
    }
    if (state.view === 'listings') renderListings();
    else if (state.view === 'orders') renderOrders();
    else renderStudio();
  }

  document.querySelectorAll('#tabs button').forEach((btn) => {
    btn.onclick = async () => {
      state.view = btn.dataset.view;
      state.selectedOrder = null;
      try {
        showError('');
        if (state.view === 'listings') await refreshListings();
        if (state.view === 'orders') await refreshOrders();
        if (state.view === 'studio') await refreshStudio();
        render();
      } catch (err) { showError(err.message); }
    };
  });

  async function acceptSession(data) {
    try {
      showError('');
      state.contentBaseUrl = data.contentBaseUrl;
      const opened = await api('/api/session', {
        method: 'POST',
        body: JSON.stringify({
          contentBaseUrl: data.contentBaseUrl,
          meUrl: data.meUrl,
          accessToken: data.accessToken,
          dpop: data.dpop,
        }),
      });
      state.token = opened.token;
      state.user = opened;
      await refreshListings();
      await refreshStudio().catch(() => { state.studio = null; });
      window.parent.postMessage({ type: 'snekl-commissions-session-ok' }, '*');
      render();
    } catch (err) {
      window.parent.postMessage({ type: 'snekl-commissions-session-failed', error: err.message }, '*');
      showError(err.message);
    }
  }

  window.addEventListener('message', (event) => {
    const data = event.data;
    if (!data || typeof data !== 'object') return;
    if (data.type === 'snekl-commissions-session') void acceptSession(data);
    if (data.type === 'snekl-commissions-sign-out') {
      if (state.token) void api('/api/session/sign-out', { method: 'POST' });
      state.token = null;
      state.user = null;
      render();
    }
  });

  const params = new URLSearchParams(location.search);
  if (params.get('content')) state.contentBaseUrl = params.get('content');

  window.parent.postMessage({ type: 'snekl-commissions-ready' }, '*');
  render();
})();
