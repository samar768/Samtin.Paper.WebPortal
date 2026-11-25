'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { useRouter } from 'next/navigation';
import { WorkspaceTabStrip } from '@/components/workspace-tab-strip';
import { salesOrderView, salesOrdersPath } from '@/lib/sales-workspace-route';
import OrderWorkspace, { type OrderWorkspaceStatus } from './OrderWorkspace';
import SalesOrdersListing from './SalesOrdersList';

type Editor = Extract<ReturnType<typeof salesOrderView>, { kind: 'editor' }>;
type Entry = Editor & { number: number; status?: OrderWorkspaceStatus };

export default function SalesWorkspaceTabs({ view, headerTarget }: {
	view: ReturnType<typeof salesOrderView>; headerTarget: HTMLDivElement | null;
}) {
	const router = useRouter();
	const [entries, setEntries] = useState<Entry[]>([]);
	const [visited, setVisited] = useState(false);
	const [closing, setClosing] = useState<string>();
	const activeKey = view.kind === 'editor' ? view.key : undefined;
	const visible = view.kind !== 'outside';
	if (visible && !visited) setVisited(true);
	if (closing && closing !== activeKey) setClosing(undefined);
	if (view.kind === 'editor' && activeKey !== closing && !entries.some(entry => entry.key === view.key)) {
		let number = 1;
		while (entries.some(entry => !entry.id && entry.number === number)) number++;
		setEntries([...entries, { ...view, number }]);
	}

	const dirty = entries.some(entry => entry.status?.dirty);
	const busy = entries.some(entry => entry.status?.busy);
	useEffect(() => {
		if (!dirty && !busy) return;
		const warn = (event: BeforeUnloadEvent) => event.preventDefault();
		const confirm = (event: Event) => {
			if (busy || !window.confirm('Discard unsaved changes in your open Sales Order tabs?')) event.preventDefault();
		};
		window.addEventListener('beforeunload', warn);
		window.addEventListener('poc:before-navigation', confirm);
		return () => {
			window.removeEventListener('beforeunload', warn);
			window.removeEventListener('poc:before-navigation', confirm);
		};
	}, [dirty, busy]);

	function updateStatus(key: string, status: OrderWorkspaceStatus) {
		setEntries(previous => previous.some(entry => entry.key === key &&
			(entry.status?.label !== status.label || entry.status?.dirty !== status.dirty || entry.status?.busy !== status.busy)
		) ? previous.map(entry => entry.key === key ? { ...entry, status } : entry) : previous);
	}

	function replaceEditor(entry: Entry, savedId?: string) {
		const target = savedId
			? salesOrderView(`${salesOrdersPath}/${encodeURIComponent(savedId)}`, new URLSearchParams())
			: null;
		setEntries(previous => {
			const alreadyOpen = target?.kind === 'editor' && previous.some(item => item.key === target.key);
			return previous.flatMap(item => item.key !== entry.key ? [item]
				: target?.kind === 'editor' && !alreadyOpen ? [{ ...target, number: item.number }] : []);
		});
		const current = salesOrderView(window.location.pathname, new URLSearchParams(window.location.search));
		if (current.kind === 'editor' && current.key === entry.key) {
			setClosing(entry.key);
			router.replace(target?.kind === 'editor' ? target.href : salesOrdersPath, { scroll: false });
		}
	}

	function close(entry: Entry) {
		if (entry.status?.busy || (entry.status?.dirty && !window.confirm('Discard unsaved changes in this Sales Order tab?'))) return;
		replaceEditor(entry);
	}

	if (!visited) return null;
	return <section hidden={!visible} inert={!visible}>
		{visible && headerTarget && createPortal(<WorkspaceTabStrip label="Sales workspace tabs"
			pinned={{ key: 'list', label: 'Sales Orders', href: salesOrdersPath, active: view.kind === 'list' }}
			tabs={entries.map(entry => ({
				key: entry.key, href: entry.href, active: entry.key === activeKey,
				label: entry.id ? entry.status?.label || 'Sales Order'
					: `Add Sales Order${entry.number > 1 ? ` ${entry.number}` : ''}`,
				dirty: entry.status?.dirty, busy: entry.status?.busy, close: () => close(entry),
			}))} />, headerTarget)}
		<div hidden={view.kind !== 'list'} inert={view.kind !== 'list'}><SalesOrdersListing active={view.kind === 'list'} /></div>
		{view.kind === 'invalid' && <p role="alert">Invalid Sales Order URL. <Link href={salesOrdersPath}>Return to Sales Orders</Link>.</p>}
		{entries.map(entry => <div key={entry.key} hidden={entry.key !== activeKey} inert={entry.key !== activeKey}>
			<OrderWorkspace id={entry.id} copyFrom={entry.copyFrom} active={entry.key === activeKey}
				onStatus={status => updateStatus(entry.key, status)} onSaved={id => replaceEditor(entry, id)}
				onDeleted={() => replaceEditor(entry)} />
		</div>)}
	</section>;
}
