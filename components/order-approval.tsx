'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { CheckCircle2, RotateCcw, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { api, errorMessage, orderSchema } from '@/lib/poc-api';
import { approvalStateSchema, type ApprovalState } from '@/lib/erp-tasks';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { RecordActionBar, type RecordAction, type RecordDetail } from '@/components/record-action-bar';

export function OrderApproval({ id, version, dirty, busy, canApprove, onBusy, onChanged, actions, primary, details, status, leading, active = true }: {
	id?: string; version?: string; dirty: boolean; busy: boolean; canApprove: boolean;
	onBusy: (busy: boolean) => void; onChanged: () => void;
	actions: RecordAction[]; primary?: RecordAction; details: RecordDetail[]; status: string;
	leading?: ReactNode; active?: boolean;
}) {
	const [state, setState] = useState<ApprovalState | null>(null);
	const [error, setError] = useState('');
	const [decision, setDecision] = useState<'Approve' | 'Reject' | 'Unapprove' | null>(null);
	const [comment, setComment] = useState('');
	useEffect(() => {
		if (!id) return;
		const controller = new AbortController();
		api(`/orders/${encodeURIComponent(id)}/approval`, approvalStateSchema, { signal: controller.signal })
			.then(result => { if (!controller.signal.aborted) { setState(result); setError(''); } })
			.catch((failure: unknown) => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
		return () => controller.abort();
	}, [id, version]);
	async function submit(event: React.FormEvent) {
		event.preventDefault();
		if (!id || !version || !state || !decision ||
			decision !== 'Unapprove' && (!state.requestId || !state.version)) return;
		onBusy(true); setError('');
		try {
			await api(`/orders/${encodeURIComponent(id)}/${decision === 'Unapprove' ? 'unapprove' : 'approval'}`, orderSchema, {
				method: 'POST', body: JSON.stringify({ version, requestId: state.requestId, requestVersion: state.version,
					...(decision === 'Unapprove' ? { reason: comment } : { decision, comment }) }),
			});
			toast.success(decision === 'Unapprove' ? 'Order approval removed' : decision === 'Reject' ? 'Rejection recorded' : 'Approval recorded');
			setDecision(null); window.dispatchEvent(new Event('poc:tasks-changed')); onChanged();
		} catch (failure) { setError(errorMessage(failure)); }
		finally { onBusy(false); }
	}
	const current = state?.stages.find(stage => stage.state === 'Active');
	const workflowActions: RecordAction[] = [];
	const choose = (value: 'Approve' | 'Reject' | 'Unapprove') => { setComment(''); setDecision(value); };
	if (state?.canAct && canApprove) workflowActions.push(
		{ id: 'approve', label: current?.step === state.stages.length ? 'Approve order' : 'Approve stage', icon: CheckCircle2, group: 'workflow', disabled: dirty, onSelect: () => choose('Approve') },
		{ id: 'reject', label: 'Reject', icon: XCircle, group: 'workflow', disabled: dirty, destructive: true, onSelect: () => choose('Reject') },
	);
	if (state?.canReverse && canApprove) workflowActions.push(
		{ id: 'unapprove', label: 'Unapprove order', icon: RotateCcw, group: 'workflow', disabled: dirty, onSelect: () => choose('Unapprove') },
	);
	const combined = [...actions.filter(action => action.group !== 'danger'), ...workflowActions, ...actions.filter(action => action.group === 'danger')];
	const reasonRequired = decision === 'Reject' || decision === 'Unapprove';
	return <>
		{id && <section className="space-y-2 rounded-lg border border-slate-200 bg-white p-3">
		<h2 className="text-xs font-semibold text-slate-900">Approval · {state?.state ?? 'Loading…'}</h2>
		{state?.canAct && dirty && <p className="text-xs text-amber-700">Save or discard your unsaved changes before deciding this approval.</p>}
		{state?.state === 'NotRequested' && <p className="text-xs text-slate-500">No web approval request exists. Existing orders are not backfilled automatically.</p>}
		{state?.state === 'Reversed' && <p className="whitespace-pre-wrap break-words text-xs text-amber-800">Unapproved by {state.reversedBy}: {state.reversalReason}. Edit and save to restart approval from stage 1.</p>}
		<ol className="space-y-2">{state?.stages.map(stage => <li key={stage.step} className={`rounded border px-3 py-2 text-xs ${stage.state === 'Active' ? 'border-blue-200 bg-blue-50' : 'border-slate-100'}`}>
			<div className="flex flex-wrap justify-between gap-1"><span className="font-medium">{stage.step}. {stage.name}</span><span>{stage.state}</span></div>
			<p className="mt-1 text-[11px] text-slate-500">{stage.group}{stage.actor && ` · ${stage.actor}`}{stage.actedAt && ` · ${new Date(stage.actedAt).toLocaleString()}`}</p>
			{stage.comment && <p className="mt-1 whitespace-pre-wrap break-words">{stage.comment}</p>}
		</li>)}</ol>
		{error && !decision && <p role="alert" className="text-xs text-red-700">{error} <button className="underline" disabled={busy || dirty} onClick={onChanged}>Reload order</button></p>}
		</section>}
		<RecordActionBar actions={combined} primary={primary} details={details} status={status} busy={busy} leading={leading} />
		<Dialog open={active && decision !== null} onOpenChange={open => { if (!open && !busy) setDecision(null); }}>
			<DialogContent className="max-w-lg"><DialogHeader><DialogTitle>{decision} order</DialogTitle>
				<DialogDescription>{decision === 'Unapprove' ? 'A reason is required and will be retained in history. Downstream-used, allocated or closed orders cannot be unapproved. The next save restarts approval from stage 1.' :
					decision === 'Reject' ? 'Provide a reason. The request will return to its creator for correction and resubmission.' : 'Only the final stage marks the ERP order approved. Approved orders cannot be edited or deleted.'}</DialogDescription>
			</DialogHeader><form onSubmit={submit} className="space-y-3">
				<label className="block space-y-1 text-xs">{decision === 'Unapprove' ? 'Reason' : 'Comment'}{reasonRequired ? ' (required)' : ' (optional)'}
					<textarea autoFocus required={reasonRequired} maxLength={2000} rows={4} disabled={busy} className="w-full rounded border p-2 text-sm" value={comment} onChange={event => setComment(event.target.value)} /></label>
				{error && <p role="alert" className="text-xs text-red-700">{error}</p>}
				<Button type="submit" disabled={busy || reasonRequired && !comment.trim()}>{busy ? 'Recording…' : `Confirm ${decision?.toLowerCase()}`}</Button>
			</form></DialogContent>
		</Dialog>
	</>;
}
