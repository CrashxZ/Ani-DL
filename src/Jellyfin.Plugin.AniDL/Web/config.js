export default function (view) {
    const id = '6c408441-bce2-4f41-a7d7-b2786f759342';
    const $ = selector => view.querySelector(selector);
    view.addEventListener('viewshow', async () => {
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(id);
        $('#ad-library-root').value = config.LibraryRoot || '';
        $('#ad-users').value = (config.AuthorizedUserIds || []).join(', ');
        $('#ad-nonadmin').checked = !!config.AllowNonAdministratorDownloads;
        $('#ad-concurrency').value = config.MaxConcurrentDownloads || 1;
        $('#ad-retries').value = config.MaxRetries == null ? 3 : config.MaxRetries;
        $('#ad-refresh-library').checked = !!config.AutoRefreshLibrary;
        Dashboard.hideLoadingMsg();
    });
    $('#AniDLConfigForm').addEventListener('submit', async event => {
        event.preventDefault(); Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(id);
        config.LibraryRoot = $('#ad-library-root').value.trim();
        config.AuthorizedUserIds = $('#ad-users').value.split(',').map(x => x.trim()).filter(Boolean);
        config.AllowNonAdministratorDownloads = $('#ad-nonadmin').checked;
        config.MaxConcurrentDownloads = Math.max(1, Math.min(4, Number($('#ad-concurrency').value)));
        config.MaxRetries = Math.max(0, Math.min(5, Number($('#ad-retries').value)));
        config.AutoRefreshLibrary = $('#ad-refresh-library').checked;
        const result = await ApiClient.updatePluginConfiguration(id, config);
        Dashboard.processPluginConfigurationUpdateResult(result);
    });
}
