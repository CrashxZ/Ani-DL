export default function (view) {
    const $ = (selector) => view.querySelector(selector);
    const state = { details: null, episodes: [] };

    function api(path, options) {
        const settings = options || {};
        return ApiClient.ajax({
            type: settings.method || 'GET',
            url: ApiClient.getUrl('AniDL/' + path, settings.query),
            data: settings.body ? JSON.stringify(settings.body) : undefined,
            contentType: settings.body ? 'application/json' : undefined,
            dataType: 'json'
        });
    }

    function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }
    function text(tag, value, className) { const node = document.createElement(tag); node.textContent = value || ''; if (className) node.className = className; return node; }
    function button(label, action) { const node = text('button', label, 'raised'); node.setAttribute('is', 'emby-button'); node.type = 'button'; node.addEventListener('click', action); return node; }
    function fail(error) { $('#ad-error').textContent = error && (error.responseText || error.message) || String(error); Dashboard.hideLoadingMsg(); }

    function renderCards(cards) {
        const host = $('#ad-results'); clear(host); clear($('#ad-details'));
        cards.forEach(card => {
            const node = document.createElement('article'); node.className = 'ad-card';
            node.append(text('h3', card.title));
            node.append(text('div', (card.mediaType || 'Anime') + ' · ' + card.subtitledEpisodes + ' sub · ' + card.dubbedEpisodes + ' dub', 'ad-muted'));
            node.addEventListener('click', () => loadDetails(card.url)); host.append(node);
        });
    }

    async function loadDetails(url) {
        Dashboard.showLoadingMsg(); $('#ad-error').textContent = '';
        try {
            const values = await Promise.all([
                api('Details', { query: { url: url, source: 'anisuge' } }),
                api('Episodes', { query: { url: url, source: 'anisuge' } })
            ]);
            state.details = values[0]; state.episodes = values[1]; renderDetails(); Dashboard.hideLoadingMsg();
        } catch (error) { fail(error); }
    }

    function renderDetails() {
        const host = $('#ad-details'); clear(host);
        host.append(text('h2', state.details.title));
        if (state.details.description) host.append(text('p', state.details.description, 'ad-muted'));
        const list = document.createElement('div'); list.className = 'ad-episodes';
        state.episodes.forEach(episode => {
            const row = document.createElement('div'); row.className = 'ad-episode ad-card';
            row.append(text('span', 'Episode ' + episode.number));
            const actions = document.createElement('div'); actions.className = 'ad-row';
            if (episode.hasJapaneseWithEnglishSubtitles) actions.append(button('Japanese + English subs', () => enqueue(episode, 'Japanese', true)));
            if (episode.hasEnglishDub) actions.append(button('English dub', () => enqueue(episode, 'EnglishDub', false)));
            row.append(actions); list.append(row);
        });
        host.append(list);
    }

    async function enqueue(episode, audio, subtitles) {
        Dashboard.showLoadingMsg();
        try {
            await api('Downloads', { method: 'POST', body: { sourceId: 'anisuge', seriesUrl: state.details.url, episodeSlug: episode.slug, seasonNumber: 1, audio: audio, includeEnglishSubtitles: subtitles } });
            Dashboard.hideLoadingMsg(); Dashboard.alert('Download queued.'); showTab('downloads'); loadJobs();
        } catch (error) { fail(error); }
    }

    async function loadJobs() {
        try {
            const jobs = await api('Downloads'); const host = $('#ad-jobs'); clear(host);
            jobs.forEach(job => {
                const node = document.createElement('div'); node.className = 'ad-job';
                const info = document.createElement('div'); info.style.flex = '1';
                info.append(text('strong', job.request.seriesTitle + ' · E' + job.request.episodeNumber));
                info.append(text('div', job.state + (job.error ? ' · ' + job.error : ''), job.error ? 'ad-error' : 'ad-status'));
                const progress = document.createElement('div'); progress.className = 'ad-progress'; const bar = document.createElement('i'); bar.style.width = Math.max(0, Math.min(100, job.progressPercent)) + '%'; progress.append(bar); info.append(progress); node.append(info);
                if (['Queued', 'Resolving', 'Downloading'].includes(job.state)) node.append(button('Cancel', async () => { await api('Downloads/' + job.id, { method: 'DELETE' }); loadJobs(); }));
                host.append(node);
            });
            if (!jobs.length) host.append(text('p', 'No downloads yet.', 'ad-muted'));
        } catch (error) { fail(error); }
    }

    async function loadCatalog(path, query) {
        Dashboard.showLoadingMsg(); $('#ad-error').textContent = '';
        try { renderCards(await api(path, { query: query })); Dashboard.hideLoadingMsg(); } catch (error) { fail(error); }
    }

    function showTab(name) {
        view.querySelectorAll('[data-panel]').forEach(node => node.hidden = node.dataset.panel !== name);
        view.querySelectorAll('[data-tab]').forEach(node => node.classList.toggle('active', node.dataset.tab === name));
    }

    $('#ad-search-form').addEventListener('submit', event => { event.preventDefault(); loadCatalog('Search', { query: $('#ad-query').value, source: 'anisuge' }); });
    $('#ad-updated').addEventListener('click', () => loadCatalog('Browse', { category: 'updated', source: 'anisuge' }));
    $('#ad-refresh').addEventListener('click', loadJobs);
    view.querySelectorAll('[data-tab]').forEach(node => node.addEventListener('click', () => { showTab(node.dataset.tab); if (node.dataset.tab === 'downloads') loadJobs(); }));
    view.addEventListener('viewshow', () => loadCatalog('Browse', { category: 'updated', source: 'anisuge' }));
}
