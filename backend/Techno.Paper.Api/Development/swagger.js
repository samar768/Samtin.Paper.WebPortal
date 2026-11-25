(() => {
    const origin = window.location.origin;
    const spec = new URL('./v1.json', window.location.href);
    const apiBase = window.location.pathname.startsWith('/api/poc/swagger/') ? '/api/poc' : '/api';
    const contextInput = document.getElementById('erp-context');
    const status = document.getElementById('context-status');
    const loadContext = document.getElementById('load-context');

    async function readJson(path) {
        const response = await fetch(`${apiBase}${path}`, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}`);
        return response.json();
    }

    loadContext.addEventListener('click', async () => {
        loadContext.disabled = true;
        contextInput.value = '';
        status.textContent = 'Loading workspace...';
        try {
            const { context } = await readJson('/session');
            if (!context?.contextVersion) throw new Error('Select a workspace in the portal first.');
            contextInput.value = context.contextVersion;
            status.textContent = `Loaded ${context.companyName} / ${context.site}.`;
        } catch (error) {
            status.textContent = error.message;
        } finally {
            loadContext.disabled = false;
        }
    });

    fetch(spec, { cache: 'no-store' }).then(async response => {
        if (!response.ok) throw new Error(`OpenAPI returned HTTP ${response.status}`);
        const document = await response.json();
        window.SwaggerUIBundle({
        spec: { ...document, servers: [{ url: origin }] },
        dom_id: '#swagger-ui',
        validatorUrl: null,
        persistAuthorization: false,
        async requestInterceptor(request) {
            const url = new URL(request.url, origin);
            if (url.origin !== origin) throw new Error('Only same-origin API requests are supported.');
            if (!url.pathname.startsWith('/api/')) throw new Error('Expected a PaperSoft API route.');

            url.pathname = apiBase + url.pathname.slice('/api'.length);
            request.url = url.href;
            request.headers ??= {};
            const context = contextInput.value.trim();
            if (context) request.headers['X-ERP-Context'] = context;
            if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method.toUpperCase())) {
                if (apiBase === '/api') throw new Error('Open Swagger through the Next.js portal to use write methods.');
                const { token } = await readJson('/csrf');
                if (!token) throw new Error('The API did not return a CSRF token.');
                request.headers['X-CSRF-TOKEN'] = token;
            }
            return request;
        },
        });
    }).catch(error => {
        document.getElementById('swagger-ui').textContent = `Unable to load Swagger: ${error.message}`;
    });
})();
