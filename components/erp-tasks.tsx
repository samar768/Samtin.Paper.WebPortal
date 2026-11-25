'use client';

import Link from 'next/link';
import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { Bell, ChevronLeft, ChevronRight, RefreshCw, Plus } from 'lucide-react';
import { z } from 'zod';
import { toast } from 'sonner';
import { api, emptySchema, errorMessage } from '@/lib/poc-api';
import { erpTaskSchema, taskInboxSchema, taskTypesSchema, type ErpTask } from '@/lib/erp-tasks';
import { useSession } from '@/components/poc-session';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

type Inbox = z.infer<typeof taskInboxSchema>;
type TaskContextValue = {
	inbox: Inbox | null; types: z.infer<typeof taskTypesSchema>; error: string; busy: boolean;
	refresh: () => Promise<void>; open: (task: ErpTask) => Promise<boolean>;
};
const TaskContext = createContext<TaskContextValue | null>(null);
function useTasks() {
	const value = useContext(TaskContext);
	if (!value) throw new Error('Task inbox provider is missing.');
	return value;
}

export function TaskProvider({ children }: { children: React.ReactNode }) {
	const session = useSession();
	const pathname = usePathname();
	const router = useRouter();
	const [inbox, setInbox] = useState<Inbox | null>(null);
	const [types, setTypes] = useState<z.infer<typeof taskTypesSchema>>([]);
	const [error, setError] = useState('');
	const [busy, setBusy] = useState(false);
	const [selected, setSelected] = useState<ErpTask | null>(null);
	const controller = useRef<AbortController | null>(null);
	const refresh = useCallback(async () => {
		const signal = controller.current?.signal;
		try {
			const [result, allowedTypes] = await Promise.all([
				api('/tasks?page=0', taskInboxSchema, { signal }), api('/tasks/types', taskTypesSchema, { signal }),
			]);
			if (!signal?.aborted) { setInbox(result); setTypes(allowedTypes); setError(''); }
		} catch (failure) { if (!signal?.aborted) setError(errorMessage(failure)); }
	}, []);
	useEffect(() => {
		const current = new AbortController();
		controller.current = current;
		void refresh();
		const update = () => { if (document.visibilityState === 'visible') void refresh(); };
		const timer = window.setInterval(update, 45000);
		document.addEventListener('visibilitychange', update);
		window.addEventListener('poc:tasks-changed', update);
		return () => {
			current.abort(); window.clearInterval(timer);
			document.removeEventListener('visibilitychange', update); window.removeEventListener('poc:tasks-changed', update);
		};
	}, [session.contextVersion, refresh]);
	async function open(task: ErpTask) {
		if (busy) return false;
		setBusy(true); setError('');
		try {
			const fresh = await api(`/tasks/${task.id}`, erpTaskSchema);
			if (fresh.href && pathname !== fresh.href &&
				!window.dispatchEvent(new Event('poc:before-navigation', { cancelable: true }))) return false;
			await api(`/tasks/${task.id}/read`, emptySchema, { method: 'POST' });
			setInbox(previous => previous ? {
				...previous, unread: Math.max(0, previous.unread - (!fresh.read && fresh.state === 'Open' ? 1 : 0)),
				items: previous.items.map(item => item.id === fresh.id ? { ...fresh, read: true } : item),
			} : previous);
			if (fresh.href) { if (pathname !== fresh.href) router.push(fresh.href); }
			else setSelected(fresh);
			return true;
		} catch (failure) { setError(errorMessage(failure)); return false; }
		finally { setBusy(false); }
	}
	return <TaskContext.Provider value={{ inbox, types, error, busy, refresh, open }}>
		{children}
		<Dialog open={selected !== null} onOpenChange={value => { if (!value) setSelected(null); }}>
			<DialogContent className="max-w-lg"><DialogHeader>
				<DialogTitle className="break-words">{selected?.title}</DialogTitle>
				<DialogDescription>{selected?.category} · {selected?.createdBy}</DialogDescription>
			</DialogHeader><p className="max-h-[60dvh] overflow-auto whitespace-pre-wrap break-words text-sm">{selected?.body}</p></DialogContent>
		</Dialog>
	</TaskContext.Provider>;
}

export function TaskNotifications() {
	const { inbox, busy, error, open, refresh } = useTasks();
	const unread = inbox?.unread ?? 0;
	const [expanded, setExpanded] = useState(false);
	return <Popover open={expanded} onOpenChange={setExpanded}><PopoverTrigger asChild>
		<Button variant="ghost" size="icon" className="relative shrink-0" aria-label={`Notifications${inbox ? `, ${inbox.unread} unread` : ''}`}>
			<Bell className="size-5" />
			{unread > 0 && <span className="absolute -right-1 -top-0.5 min-w-4 rounded-full bg-blue-600 px-1 text-[9px] leading-4 text-white">{Math.min(unread, 99)}{unread > 99 ? '+' : ''}</span>}
			{error && <span className="absolute right-0 top-0 text-xs text-red-600" aria-label="Notifications could not refresh">!</span>}
		</Button>
	</PopoverTrigger><PopoverContent align="end" className="w-[min(22rem,calc(100vw-2rem))] p-3">
		<div className="mb-2 flex items-center justify-between"><h2 className="text-sm font-semibold">Notifications</h2>
			<Button size="icon" variant="ghost" aria-label="Refresh notifications" onClick={() => void refresh()}><RefreshCw className="size-3.5" /></Button></div>
		{error && <p role="alert" className="mb-2 text-xs text-red-700">{error}</p>}
		<div className="max-h-72 space-y-1 overflow-y-auto">
			{!inbox && !error && <p className="text-xs text-slate-500">Loading notifications…</p>}
			{inbox?.items.slice(0, 6).map(task => <button key={task.id} disabled={busy} onClick={async () => { if (await open(task)) setExpanded(false); }}
				className={`block w-full rounded p-2 text-left text-xs hover:bg-blue-50 ${task.read ? 'text-slate-500' : 'bg-slate-50 text-slate-900'}`}>
				<span className="block break-words font-medium">{task.title}</span><span className="mt-1 block text-[10px]">{task.category} · {task.state}</span>
			</button>)}
			{inbox?.items.length === 0 && <p className="p-2 text-xs text-slate-500">No tasks in this workspace.</p>}
		</div>
		<Link href="/dashboard#tasks" onClick={() => setExpanded(false)} className="mt-3 block text-xs font-medium text-blue-600">View task inbox</Link>
	</PopoverContent></Popover>;
}

export function TaskInbox() {
	const { inbox, types, error: sharedError, busy, open, refresh } = useTasks();
	const [page, setPage] = useState(0);
	const [later, setLater] = useState<Inbox | null>(null);
	const [error, setError] = useState('');
	const [attempt, setAttempt] = useState(0);
	const [publishing, setPublishing] = useState(false);
	const [editor, setEditor] = useState(false);
	const [type, setType] = useState('');
	const [title, setTitle] = useState('');
	const [body, setBody] = useState('');
	const key = useRef<string | null>(null);
	useEffect(() => {
		if (!page) return;
		const controller = new AbortController();
		api(`/tasks?page=${page}`, taskInboxSchema, { signal: controller.signal }).then(result => { if (!controller.signal.aborted) { setLater(result); setError(''); } })
			.catch((failure: unknown) => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
		return () => controller.abort();
	}, [page, attempt]);
	const data = page ? later : inbox;
	async function publish(event: React.FormEvent) {
		event.preventDefault(); setPublishing(true); setError('');
		try {
			key.current ??= crypto.randomUUID();
			await api('/tasks', erpTaskSchema, { method: 'POST', body: JSON.stringify({ type: type || types[0]?.code, title, body, idempotencyKey: key.current }) });
			toast.success('Task published');
			setEditor(false); setPage(0); await refresh();
		} catch (failure) { setError(errorMessage(failure)); }
		finally { setPublishing(false); }
	}
	return <section id="tasks" className="scroll-mt-4 space-y-3 rounded-lg border border-slate-200 bg-white p-3 sm:p-4">
		<div className="flex flex-wrap items-center justify-between gap-2"><div><h2 className="font-semibold text-slate-900">Tasks</h2>
			<p className="text-xs text-slate-500">Your authorized company, financial year and site · {inbox?.unread ?? 0} unread</p></div>
			<div className="flex gap-2">
				<Button variant="outline" size="sm" onClick={() => { setLater(null); setAttempt(value => value + 1); void refresh(); }}><RefreshCw className="size-3.5" />Refresh</Button>
				{types.length > 0 && <Button size="sm" onClick={() => { setType(types[0].code); setTitle(''); setBody(''); setError(''); key.current = null; setEditor(true); }}><Plus className="size-3.5" />New task</Button>}
			</div></div>
		{(sharedError || error) && !editor && <p role="alert" className="text-xs text-red-700">{error || sharedError}</p>}
		<div className="overflow-auto"><table className="w-full min-w-[560px] text-left text-xs">
			<thead className="border-y border-slate-100 bg-slate-50 text-slate-500"><tr><th className="p-2">Task</th><th className="p-2">Category</th><th className="p-2">Status</th><th className="p-2">Published</th></tr></thead>
			<tbody>{data?.items.map(task => <tr key={task.id} className="border-b border-slate-100">
				<td className="p-2"><button disabled={busy} onClick={async () => {
					if (await open(task) && page) setLater(previous => previous ? { ...previous, items: previous.items.map(item => item.id === task.id ? { ...item, read: true } : item) } : previous);
				}} className={`text-left text-blue-600 hover:underline ${task.read ? '' : 'font-semibold'}`}>{task.title}</button><span className="block text-[10px] text-slate-400">{task.createdBy}</span></td>
				<td className="p-2">{task.category}</td><td className="p-2"><span className={`rounded px-1.5 py-0.5 ${task.state === 'Rejected' ? 'bg-red-50 text-red-700' : task.state === 'Completed' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-600'}`}>{task.state}</span></td>
				<td className="whitespace-nowrap p-2 text-slate-500">{new Date(task.createdAt).toLocaleString()}</td>
			</tr>)}</tbody>
		</table></div>
		{!data && <p className="text-xs text-slate-500">Loading tasks…</p>}
		{data?.items.length === 0 && <p className="text-xs text-slate-500">No published tasks are available to you in this workspace.</p>}
		<div className="flex items-center justify-end gap-2 text-xs text-slate-500">Page {page + 1}
			<Button variant="outline" size="icon" className="size-7" aria-label="Previous task page" disabled={!page} onClick={() => { setLater(null); setPage(value => value - 1); }}><ChevronLeft className="size-3.5" /></Button>
			<Button variant="outline" size="icon" className="size-7" aria-label="Next task page" disabled={!data?.hasMore} onClick={() => { setLater(null); setPage(value => value + 1); }}><ChevronRight className="size-3.5" /></Button>
		</div>
		<Dialog open={editor} onOpenChange={value => { if (!publishing) setEditor(value); }}>
			<DialogContent className="max-w-lg"><DialogHeader><DialogTitle>Publish task</DialogTitle><DialogDescription>Recipients come from your administrator&apos;s task-type group configuration.</DialogDescription></DialogHeader>
				<form onSubmit={publish} className="space-y-3">
					<label className="block space-y-1 text-xs">Category<select aria-label="Task category" className="h-9 w-full rounded border px-2" disabled={publishing} value={type} onChange={event => setType(event.target.value)}>{types.map(option => <option key={option.code} value={option.code}>{option.name}</option>)}</select></label>
					<label className="block space-y-1 text-xs">Title<Input required maxLength={200} disabled={publishing} value={title} onChange={event => setTitle(event.target.value)} /></label>
					<label className="block space-y-1 text-xs">Message<textarea required maxLength={2000} rows={6} disabled={publishing} value={body} onChange={event => setBody(event.target.value)} className="w-full resize-y rounded border p-2 text-sm" /></label>
					{error && <p role="alert" className="text-xs text-red-700">{error}</p>}
					<Button type="submit" disabled={publishing || !title.trim() || !body.trim()}>{publishing ? 'Publishing…' : 'Publish'}</Button>
				</form></DialogContent>
		</Dialog>
	</section>;
}
