'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { ArrowRight, ChevronLeft, ChevronRight, RefreshCw, Search, Settings2, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useSession } from '@/components/poc-session';
import { api, errorMessage, rowsSchema, type DataRow, type Session } from '@/lib/poc-api';
import { RecordCreateLink } from '@/components/record-create-link';
import { ListingSettings } from '@/components/listing-settings';
import { useListingPreferences } from '@/lib/use-listing-preferences';
import { defaultListingPreferences, visibleListingColumns, type ListingDefinition, type ListingPreferences } from '@/lib/listing-preferences';

const listing: ListingDefinition = {
	key: 'sales-orders', sortColumn: 'date',
	columns: [
		{ id: 'reference', label: 'Reference', width: 140 },
		{ id: 'date', label: 'Date', width: 130 },
		{ id: 'buyer', label: 'Buyer', width: 300 },
		{ id: 'gross', label: 'Gross amount', width: 160 },
		{ id: 'state', label: 'State', width: 130 },
	],
};

export default function SalesOrdersListing({ active }: { active: boolean }) {
	const session = useSession();
	return <PersonalizedSalesOrders key={session.contextVersion} session={session} active={active} />;
}

function PersonalizedSalesOrders({ session, active }: { session: Session; active: boolean }) {
	const settings = useListingPreferences(listing);
	const [resetting, setResetting] = useState(false);
	const [resetError, setResetError] = useState('');
	if (!settings.preferences) return <section className="space-y-3">
		<h1 className="text-xl font-semibold">Sales orders</h1>
		<p role={settings.error ? 'alert' : 'status'}>{settings.error || 'Loading your listing settings…'}</p>
		{settings.error && <div className="flex flex-wrap gap-2">
			<Button variant="outline" disabled={resetting} onClick={settings.retry}>Retry</Button>
			<Button variant="outline" disabled={resetting} onClick={async () => {
				setResetting(true); setResetError('');
				try { await settings.reset(); } catch (failure: unknown) { setResetError(errorMessage(failure)); }
				finally { setResetting(false); }
			}}>Reset my listing defaults</Button>
		</div>}
		{resetError && <p role="alert" className="text-red-700">{resetError}</p>}
	</section>;
	return <SalesOrdersList session={session} active={active} preferences={settings.preferences} onSave={settings.save} onReset={settings.reset} />;
}

function SalesOrdersList({ session, preferences, onSave, onReset, active }: {
	session: Session; preferences: ListingPreferences; active: boolean;
	onSave: (value: ListingPreferences) => Promise<void>; onReset: () => Promise<void>;
}) {
	const [rows, setRows] = useState<DataRow[]>([]);
	const [search, setSearch] = useState('');
	const [filter, setFilter] = useState('');
	const [page, setPage] = useState(0);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState('');
	const [attempt, setAttempt] = useState(0);
	const [settingsOpen, setSettingsOpen] = useState(false);
	const columns = visibleListingColumns(preferences);
	const labels = new Map(listing.columns.map(column => [column.id, column.label]));
	const rowSpacing = preferences.density === 'compact' ? 'px-3 py-2' : 'px-3 py-4';
	const tableWidth = columns.reduce((width, column) => width + column.width, 90);
	function settingsApplied(next: ListingPreferences) {
		if (page !== 0 || next.pageSize !== preferences.pageSize || next.sortColumn !== preferences.sortColumn || next.sortDirection !== preferences.sortDirection)
			setLoading(true);
		setPage(0);
	}
	useEffect(() => {
		const controller = new AbortController();
		api(`/orders?search=${encodeURIComponent(filter)}&page=${page}&pageSize=${preferences.pageSize}&sort=${encodeURIComponent(preferences.sortColumn)}&direction=${preferences.sortDirection}`, rowsSchema, { signal: controller.signal })
			.then((records) => { setRows(records); setError(''); setLoading(false); })
			.catch((failure: unknown) => { if (!controller.signal.aborted) { setError(errorMessage(failure)); setLoading(false); } });
		return () => controller.abort();
	}, [filter, page, attempt, preferences.pageSize, preferences.sortColumn, preferences.sortDirection]);
	useEffect(() => {
		const refresh = () => { setLoading(true); setAttempt(value => value + 1); };
		window.addEventListener('poc:sales-orders-changed', refresh);
		return () => window.removeEventListener('poc:sales-orders-changed', refresh);
	}, []);
	return <section className="space-y-4">
		<div className="flex flex-wrap gap-4 items-center justify-between"><div>
			<p className="text-[11px] uppercase tracking-wider text-slate-400">Sales module</p>
			<h1 className="text-xl text-slate-900 font-semibold mt-1">Sales orders</h1>
			<p className="text-xs text-slate-400 mt-1">FY {session.financialYear} · {session.site} · SORD</p>
			{session.readOnly && <p className="mt-1 text-xs text-amber-800">Read-only financial year/site</p>}
		</div></div>
		<div className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-200 bg-white p-2" role="group" aria-label="Sales order actions">
			{session.capabilities.add && <RecordCreateLink href="/dashboard/sale-order/new" label="New sales order" />}
		<form className="order-last flex flex-1 basis-full gap-2 min-w-0 sm:order-none sm:basis-auto sm:max-w-md" role="search" aria-label="Search sales orders" onSubmit={(event) => {
			event.preventDefault(); setLoading(true); setFilter(search); setPage(0); setAttempt((value) => value + 1);
		}}><Input className="h-8 bg-white text-xs md:text-xs" aria-label="Search order reference or buyer" placeholder="Search reference or buyer…" value={search}
			maxLength={100} onChange={(event) => setSearch(event.target.value)} /><Button className="size-8" type="submit" variant="ghost" aria-label="Search" title="Search orders"><Search className="size-4" /></Button></form>
			{(search || filter) && <Button className="size-8" variant="ghost" aria-label="Clear search" title="Clear search" onClick={() => {
				setSearch(''); setFilter(''); setPage(0); setLoading(true); setAttempt((value) => value + 1);
			}}><X className="size-4" /></Button>}
			<Button className="size-8" variant="ghost" disabled={loading} aria-label="Refresh orders" title="Refresh orders" onClick={() => {
				setLoading(true); setAttempt((value) => value + 1);
			}}><RefreshCw className={`size-4 ${loading ? 'animate-spin' : ''}`} /></Button>
			<Button className="size-8" variant="ghost" aria-label="Listing settings" title="Customize this listing" onClick={() => setSettingsOpen(true)}><Settings2 className="size-4" /></Button>
			<span className="ml-auto text-[11px] text-slate-500" role="status">{loading ? 'Loading...' : `${rows.length} orders on this page`}</span>
		</div>
		{active && settingsOpen && <ListingSettings definition={listing} value={preferences} onClose={() => setSettingsOpen(false)}
			onSave={async next => { await onSave(next); settingsApplied(next); }}
			onReset={async () => { await onReset(); settingsApplied(defaultListingPreferences(listing)); }} />}
		{error && <div role="alert" className="text-red-700">{error} <Button variant="outline" onClick={() => {
			setLoading(true); setAttempt((value) => value + 1);
		}}>Retry</Button></div>}
		{loading ? <p role="status">Loading orders…</p> : !error && <div className="overflow-auto rounded-lg border border-slate-200 bg-white">
			<table className="table-fixed text-xs" aria-label="Sales orders" style={{ width: tableWidth }}>
				<colgroup>{columns.map(column => <col key={column.id} style={{ width: column.width }} />)}<col style={{ width: 90 }} /></colgroup>
				<thead className="text-left text-slate-500 bg-slate-50">
				<tr>{columns.map(column => <th className="px-3 py-2" scope="col" key={column.id}>{labels.get(column.id)}</th>)}<th className="relative" scope="col"><span className="sr-only">Actions</span></th></tr>
			</thead><tbody>{rows.map((row) => <tr key={row.DocId} className="border-t border-slate-100 hover:bg-slate-50">
				{columns.map(column => <td className={`${rowSpacing} break-words ${column.id === 'gross' ? 'tabular-nums' : ''}`} key={column.id}>
					{column.id === 'reference' ? <Link className="font-medium text-blue-700 hover:underline" href={`/dashboard/sale-order/${encodeURIComponent(row.DocId ?? '')}`}>{row.RecId}</Link>
						: column.id === 'date' ? row.V_Date
							: column.id === 'buyer' ? row.Party
								: column.id === 'gross' ? row.GAmount ?? '0'
									: <span className={`inline-flex rounded-full px-2 py-0.5 text-[10px] ${row.ApprovedBy ? 'bg-green-50 text-green-800' : 'bg-slate-100 text-slate-600'}`}>{row.ApprovedBy ? 'Approved' : 'Unapproved'}</span>}
				</td>)}
				<td className={rowSpacing}><Link className="inline-flex items-center gap-1 text-blue-700 hover:underline" aria-label={`Open order ${row.RecId}`} href={`/dashboard/sale-order/${encodeURIComponent(row.DocId ?? '')}`}>Open<ArrowRight className="size-3" aria-hidden="true" /></Link></td>
			</tr>)}</tbody></table>
			{!rows.length && <p className="p-6 text-slate-400">No authorized matching orders.</p>}
		</div>}
		<div className="flex items-center gap-4">
			<Button variant="outline" disabled={loading || page === 0} onClick={() => { setLoading(true); setPage((value) => value - 1); }}><ChevronLeft className="size-4" />Previous</Button>
			<span className="text-sm text-slate-400">Page {page + 1}</span>
			<Button variant="outline" disabled={loading || rows.length < preferences.pageSize || page >= 10000} onClick={() => { setLoading(true); setPage((value) => value + 1); }}>Next<ChevronRight className="size-4" /></Button>
		</div>
	</section>;
}
