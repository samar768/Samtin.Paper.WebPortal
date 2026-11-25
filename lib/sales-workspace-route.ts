export const salesOrdersPath = '/dashboard/sale-order';
export const newSalesOrderPath = `${salesOrdersPath}/new`;

export function newSalesOrderHref(copyFrom?: string) {
	const search = new URLSearchParams({ draft: crypto.randomUUID() });
	if (copyFrom) search.set('copyFrom', copyFrom);
	return `${newSalesOrderPath}?${search}`;
}

export function salesOrderView(path: string, search: Pick<URLSearchParams, 'getAll'>) {
	if (path === salesOrdersPath) return { kind: 'list' } as const;
	if (path === newSalesOrderPath) {
		const drafts = search.getAll('draft');
		const sources = search.getAll('copyFrom');
		if (drafts.length > 1 || sources.length > 1 ||
			(drafts.length && !/^[a-f0-9-]{36}$/i.test(drafts[0])))
			return { kind: 'invalid' } as const;
		const copyFrom = sources[0] || undefined;
		const params = new URLSearchParams();
		if (drafts[0]) params.set('draft', drafts[0]);
		if (copyFrom) params.set('copyFrom', copyFrom);
		return { kind: 'editor', key: JSON.stringify(['draft', drafts[0], copyFrom]),
			href: params.size ? `${path}?${params}` : path, copyFrom, id: undefined } as const;
	}
	if (!path.startsWith(`${salesOrdersPath}/`)) return { kind: 'outside' } as const;
	try {
		const id = decodeURIComponent(path.slice(salesOrdersPath.length + 1));
		if (id && !id.includes('/')) return { kind: 'editor', key: JSON.stringify(['order', id]),
			href: `${salesOrdersPath}/${encodeURIComponent(id)}`, id, copyFrom: undefined } as const;
	} catch (error) {
		if (!(error instanceof URIError)) throw error;
	}
	return { kind: 'invalid' } as const;
}
