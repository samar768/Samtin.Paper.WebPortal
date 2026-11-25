import type { NavigationItem } from './poc-api';

export type MenuNode = NavigationItem & { children: MenuNode[] };

export function menuCreateHref(item: NavigationItem, canAdd: boolean): string | null {
	return canAdd && item.implemented && item.href === '/dashboard/sale-order'
		? '/dashboard/sale-order/new' : null;
}

export function menuTree(items: NavigationItem[]): MenuNode[] {
	const nodes = new Map<string, MenuNode>();
	for (const item of items) {
		if (nodes.has(item.key)) throw new Error('Duplicate menu identity. Contact the administrator.');
		nodes.set(item.key, { ...item, children: [] });
	}
	const roots: MenuNode[] = [];
	for (const item of [...nodes.values()].sort((a, b) => a.order - b.order)) {
		if (!item.parentKey) roots.push(item);
		else {
			const parent = nodes.get(item.parentKey);
			if (!parent) throw new Error('A menu parent is missing. Contact the administrator.');
			parent.children.push(item);
		}
	}
	let count = 0;
	const visit = (node: MenuNode) => { count++; node.children.forEach(visit); };
	roots.forEach(visit);
	if (count !== nodes.size) throw new Error('The menu hierarchy contains a cycle. Contact the administrator.');
	return roots;
}

export function filterMenu(nodes: MenuNode[], query: string): MenuNode[] {
	const search = query.trim().toLocaleLowerCase();
	if (!search) return nodes;
	return nodes.flatMap((node) => {
		if (node.label.toLocaleLowerCase().includes(search)) return [node];
		const children = filterMenu(node.children, query);
		return children.length ? [{ ...node, children }] : [];
	});
}
