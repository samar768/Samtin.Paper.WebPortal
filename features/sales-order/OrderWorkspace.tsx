'use client';

import Link from 'next/link';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { z } from 'zod';
import { CheckCheck, Copy, Paperclip, Pencil, Plus, Printer, ReceiptText, Save, SlidersHorizontal, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { OrderField, fieldLabel } from '@/components/order-field';
import { VoucherTermsEditor } from '@/components/voucher-terms-editor';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { useSession } from '@/components/poc-session';
import { OrderApproval } from '@/components/order-approval';
import { ExpenseEditor } from '@/components/expense-editor';
import { GridScrollViewport } from '@/components/grid-scroll-viewport';
import { QuantityToleranceEditor, toleranceSchema } from '@/components/quantity-tolerance-editor';
import { useDebouncedCalculation } from '@/lib/use-debounced-calculation';
import { applyCalculation, calculationSignature, itemExpenseCalculationSignature, retainCalculatedExpenseAmounts, type DraftCalculation } from '@/lib/order-calculation';
import type { RecordAction } from '@/components/record-action-bar';
import {
	ApiError, api, emptySchema, errorMessage, fetchOrderPdf, loadOrderVoucherTypes, metadataSchema, orderSchema, rowSchema, type OrderVoucherType,
	savePayload, type DataRow, type Field, type OrderMetadata,
} from '@/lib/poc-api';
import { applyItemExpenses, applyItemExpenseRatesToAll, itemDraft, orderDraft, orderPayload, type OrderDraft } from '@/lib/order-draft';
import { editEntryRow, ensureEntryRow } from '@/lib/entry-rows';
import { bindFieldErrors, clearFieldError, type FieldErrors } from '@/lib/field-validation';
import { isVisibleOrderField, summarizeOrderItems } from '@/lib/order-item-summary';
import { newSalesOrderHref } from '@/lib/sales-workspace-route';

const lineTemplateSchema = z.object({ values: rowSchema, expenses: z.array(rowSchema) });
const itemExpenseDefaultsSchema = z.array(z.object({ expCode: z.string(), per: z.string(), amount: z.string() }));
const calculationSchema = z.object({ header: rowSchema, lines: z.array(rowSchema),
	headerExpenses: z.array(rowSchema), lineExpenses: z.array(z.array(rowSchema)) });
type LineTemplate = z.infer<typeof lineTemplateSchema>;
const lineLimit = 100;
function itemColumnWidth(field: Field) {
	if (field.key === 'ItemCode' || field.key === 'ItemDesc') return 108;
	if (['date', 'smalldatetime', 'datetime', 'datetime2'].includes(field.type)) return 128;
	if (field.lookup || field.valueHelp || field.type === 'bit') return 52;
	if (['int', 'smallint', 'bigint', 'tinyint', 'decimal', 'numeric', 'float', 'real', 'money', 'smallmoney'].includes(field.type)) return 56;
	return 96;
}
function withEntryRow(draft: OrderDraft, template: LineTemplate | null, writable: boolean): OrderDraft {
	return template && writable ? { ...draft, items: ensureEntryRow(draft.items,
		() => itemDraft(template.values, template.expenses), lineLimit) } : draft;
}

function OrderPdfPreview({ id, disabled }: { id: string | null; disabled: boolean }) {
	const [open, setOpen] = useState(false);
	return <Dialog open={open} onOpenChange={setOpen}>
		<DialogTrigger asChild>
			<Button type="button" size="sm" variant="ghost" className="h-8 text-xs" disabled={disabled || !id}>
				<Printer className="size-3.5" />Print PDF
			</Button>
		</DialogTrigger>
		{open && id && <DialogContent className="flex h-[min(90dvh,60rem)] flex-col gap-3 p-4 sm:max-w-5xl">
			<DialogHeader className="pr-10">
				<DialogTitle>Sales Order report</DialogTitle>
				<DialogDescription>Preview the saved order without leaving the ERP.</DialogDescription>
			</DialogHeader>
			<OrderPdfDocument id={id} />
		</DialogContent>}
	</Dialog>;
}

function OrderPdfDocument({ id }: { id: string }) {
	const [result, setResult] = useState<{ url: string } | { error: string } | null>(null);
	useEffect(() => {
		const controller = new AbortController();
		let url: string | undefined;
		void fetchOrderPdf(id, controller.signal).then(pdf => {
			if (controller.signal.aborted) return;
			url = URL.createObjectURL(pdf);
			setResult({ url });
		}).catch((failure: unknown) => {
			if (!controller.signal.aborted) setResult({ error: errorMessage(failure) });
		});
		return () => {
			controller.abort();
			if (url) URL.revokeObjectURL(url);
		};
	}, [id]);

	if (!result) return <p role="status" className="text-sm">Generating report…</p>;
	if ('error' in result) return <p role="alert" className="text-sm text-red-700">{result.error}</p>;
	return <>
		{navigator.pdfViewerEnabled === false
			? <p role="status" className="text-sm">This browser cannot display PDFs inline. Use Download PDF to view the report.</p>
			: <iframe title="Sales Order PDF preview" src={result.url} className="min-h-0 w-full flex-1 rounded-md border" />}
		<DialogFooter>
			<Button asChild variant="outline" size="sm"><a href={result.url} download="Sales-Order-Sample.pdf">Download PDF</a></Button>
		</DialogFooter>
	</>;
}

export type OrderWorkspaceStatus = { label?: string; dirty: boolean; busy: boolean };

export default function OrderWorkspace({ id, copyFrom, active, onStatus, onSaved, onDeleted }: {
	id?: string; copyFrom?: string; active: boolean;
	onStatus: (status: OrderWorkspaceStatus) => void; onSaved: (id: string) => void; onDeleted: () => void;
}) {
	const session = useSession();
	const router = useRouter();
	const [order, setOrder] = useState<OrderDraft | null>(null);
	const [metadata, setMetadata] = useState<OrderMetadata | null>(null);
	const [error, setError] = useState('');
	const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
	const [notice, setNotice] = useState('');
	const [busy, setBusy] = useState(false);
	const [dirty, setDirty] = useState(false);
	const [attempt, setAttempt] = useState(0);
	const [approvalRevision, setApprovalRevision] = useState(0);
	const [taxEditor, setTaxEditor] = useState<{ key: string; expenses: DataRow[]; dirty: boolean;
		preview?: { signature: string; rows?: DataRow[]; message?: string; errors?: FieldErrors } } | null>(null);
	const [toleranceEditor, setToleranceEditor] = useState<{ key: string; dirty: boolean } | null>(null);
	const [lastCalculation, setLastCalculation] = useState<string | null>(null);
	const [calculationFailure, setCalculationFailure] = useState<{ signature: string; message: string; errors: FieldErrors } | null>(null);
	const [lineTemplate, setLineTemplate] = useState<LineTemplate | null>(null);
	const [voucherTypes, setVoucherTypes] = useState<OrderVoucherType[] | null>([]);
	const [automaticReference, setAutomaticReference] = useState(true);
	const gridRef = useRef<HTMLDivElement | null>(null);
	const workspaceRef = useRef<HTMLDivElement | null>(null);
	const taxContentRef = useRef<HTMLDivElement | null>(null);
	const toleranceContentRef = useRef<HTMLDivElement | null>(null);
	const pendingValidationFocus = useRef(false);
	const taxTrigger = useRef<HTMLButtonElement | null>(null);
	const requestKey = useRef<string | null>(null);
	const beforePendingSave = useRef<string | null>(null);
	const canWrite = Boolean(order && !order.locked && (order.id ? session.capabilities.edit : session.capabilities.add));
	useEffect(() => {
		const controller = new AbortController();
		Promise.all([
			api('/metadata/orders', metadataSchema, { signal: controller.signal }),
			api(id ? `/orders/${encodeURIComponent(id)}` : copyFrom ? `/orders/${encodeURIComponent(copyFrom)}/copy` : '/orders/defaults', orderSchema, { signal: controller.signal }),
			session.capabilities.add || session.capabilities.edit
				? api('/orders/line-defaults', lineTemplateSchema, { signal: controller.signal }) : Promise.resolve(null),
			!id && session.capabilities.add
				? loadOrderVoucherTypes(controller.signal) : Promise.resolve([]),
		]).then(([schema, record, template, vouchers]) => {
			setLineTemplate(template); setAutomaticReference(!record.id);
			setVoucherTypes(vouchers);
			setMetadata(schema); setOrder(withEntryRow(orderDraft(record), template,
				!record.locked && (record.id ? session.capabilities.edit : session.capabilities.add)));
			setDirty(Boolean(copyFrom && !id)); setError(''); setFieldErrors({}); setTaxEditor(null);
			setToleranceEditor(null); setCalculationFailure(null); setLastCalculation(null);
			setNotice(copyFrom && !id ? 'Copied into a new unsaved order. Numbering and dates are fresh; enter the customer PO reference and review all values before saving.' : '');
			requestKey.current = null; beforePendingSave.current = null;
		}).catch((failure: unknown) => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
		return () => controller.abort();
	}, [id, copyFrom, attempt, session.capabilities.add, session.capabilities.edit]);
	const liveSignature = active && order && metadata && dirty && canWrite && !busy && !taxEditor && !toleranceEditor
		? calculationSignature(order, metadata) : null;
	const loadCalculation = useCallback((signature: string, signal: AbortSignal) => {
		const body: { request: unknown } = JSON.parse(signature);
		return api('/orders/draft-calculation', calculationSchema, { method: 'POST', body: JSON.stringify(body.request), signal });
	}, []);
	const calculateTolerance = useCallback((body: string, signal: AbortSignal) =>
		api('/orders/tolerance', toleranceSchema, { method: 'POST', body, signal }), []);
	const calculated = useCallback((result: DraftCalculation, signature: string) => {
		if (!metadata) return;
		setOrder((previous) => previous ? applyCalculation(previous, metadata, signature, result) : previous);
		setLastCalculation(signature); setCalculationFailure(null);
	}, [metadata]);
	const calculationFailed = useCallback((failure: unknown, signature: string) => {
		const snapshot: { keys: string[] } = JSON.parse(signature);
		setCalculationFailure({ signature, message: errorMessage(failure),
			errors: failure instanceof ApiError ? bindFieldErrors(failure.errors, snapshot.keys) : {} });
	}, []);
	useDebouncedCalculation(liveSignature, loadCalculation, calculated, calculationFailed);
	const taxKey = taxEditor?.key;
	const taxSignature = active && order && metadata && taxEditor?.dirty && canWrite && !busy
		? itemExpenseCalculationSignature(order, metadata, taxEditor.key, taxEditor.expenses) : null;
	const taxCalculated = useCallback((result: DraftCalculation, signature: string) => {
		const { keys }: { keys: string[] } = JSON.parse(signature);
		const rows = result.lineExpenses[keys.indexOf(taxKey ?? '')];
		setTaxEditor(editor => editor && editor.key === taxKey ? { ...editor, preview: rows ? { signature, rows }
			: { signature, message: 'Calculated expenses do not match this item. Reopen the dialog.' } } : editor);
	}, [taxKey]);
	const taxCalculationFailed = useCallback((failure: unknown, signature: string) => {
		const { keys }: { keys: string[] } = JSON.parse(signature);
		setTaxEditor(editor => editor && editor.key === taxKey ? { ...editor, preview: { signature,
			message: errorMessage(failure), errors: failure instanceof ApiError ? bindFieldErrors(failure.errors, keys) : {} } } : editor);
	}, [taxKey]);
	useDebouncedCalculation(taxSignature, loadCalculation, taxCalculated, taxCalculationFailed);
	const taxPreview = taxEditor?.preview?.signature === taxSignature ? taxEditor?.preview : undefined;
	const liveFailure = calculationFailure?.signature === liveSignature ? calculationFailure : null;
	const displayedErrors = { ...fieldErrors, ...liveFailure?.errors };
	useEffect(() => {
		if (!active || busy || !pendingValidationFocus.current) return;
		const frame = requestAnimationFrame(() => {
			const controls = [workspaceRef.current, taxContentRef.current, toleranceContentRef.current].flatMap((root) =>
				root ? Array.from(root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLButtonElement>('[data-validation-path]')) : []);
			const control = Object.keys(fieldErrors).map((path) => controls.find((input) =>
				input.dataset.validationPath === path && !input.disabled)).find(Boolean);
			control?.focus();
			control?.scrollIntoView({ block: 'center', inline: 'nearest' });
			pendingValidationFocus.current = false;
		});
		return () => cancelAnimationFrame(frame);
	}, [fieldErrors, active, busy, taxEditor, toleranceEditor]);
	function edited(path: string) { setFieldErrors((previous) => clearFieldError(previous, path)); }
	const hasChanges = dirty || Boolean(taxEditor?.dirty || toleranceEditor?.dirty);
	const tabLabel = id ? order?.header.RecId?.trim() : undefined;
	useEffect(() => { onStatus({ label: tabLabel, dirty: hasChanges, busy }); }, [onStatus, tabLabel, hasChanges, busy]);
	const changed = useCallback((next: OrderDraft | ((previous: OrderDraft) => OrderDraft)) => {
		setOrder((previous) => previous ? typeof next === 'function' ? next(previous) : next : previous);
		setDirty(true); setNotice('');
		// A changed payload after an uncertain submission must not silently reuse a different request key.
	}, []);
	function addLine() {
		const entry = gridRef.current?.querySelector<HTMLElement>('[data-entry-row="true"] input, [data-entry-row="true"] select, [data-entry-row="true"] button[role="combobox"]');
		entry?.focus();
		entry?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
	}
	async function selectVoucher(code: string) {
		if (!order || order.id || busy || code === order.header.V_Type) return;
		if (dirty && !window.confirm('Changing voucher type discards this unsaved draft. Continue?')) return;
		setBusy(true); setError('');
		try {
			const query = new URLSearchParams({ voucherType: code });
			const [record, template] = await Promise.all([
				api(`/orders/defaults?${query}`, orderSchema),
				api(`/orders/line-defaults?${query}`, lineTemplateSchema),
			]);
			if (record.metadataVersion !== metadata?.version) throw new Error('Order metadata changed. Reload before selecting a voucher.');
			setLineTemplate(template);
			setOrder(withEntryRow(orderDraft(record), template, canWrite));
			setDirty(false); setAutomaticReference(true); setNotice(''); setFieldErrors({});
			setLastCalculation(null); setCalculationFailure(null);
			requestKey.current = null; beforePendingSave.current = null;
		} catch (failure) {
			setError(errorMessage(failure));
		} finally {
			setBusy(false);
		}
	}
	async function changeItem(key: string, value: string | null) {
		if (!lineTemplate || busy) return;
		setBusy(true);
		setError('');
		try {
			const defaults = value ? await api(`/orders/item-expense-defaults?${new URLSearchParams({ itemCode: value })}`,
				itemExpenseDefaultsSchema) : [];
			const byCode = new Map(defaults.map(row => [row.expCode.toUpperCase(), row]));
			changed(previous => !previous.items.some(row => row.key === key) ? previous : ({ ...previous, items: editEntryRow(previous.items, key, row => ({
				...row, values: { ...row.values, ItemCode: value },
				expenses: row.expenses.map(expense => {
					const tax = byCode.get(expense.ExpCode?.trim().toUpperCase() ?? '');
					return tax ? { ...expense, Per: tax.per, Amount: tax.amount } : expense;
				}),
			}), () => itemDraft(lineTemplate.values, lineTemplate.expenses), lineLimit) }));
		} catch (failure) {
			setError(errorMessage(failure));
		} finally {
			setBusy(false);
		}
	}
	async function submit(preview: boolean) {
		if (!order || !metadata || (order.header.V_Type && order.header.V_Type !== 'SORD')) return;
		setBusy(true); setError(''); setFieldErrors({}); setNotice('');
		try {
			requestKey.current ??= crypto.randomUUID();
			const payload = savePayload(orderPayload(order), metadata, requestKey.current, !order.id && automaticReference);
			const signature = JSON.stringify(payload);
			if (!preview && beforePendingSave.current && beforePendingSave.current !== signature) {
				throw new Error('The previous save has an uncertain result. Reload the list and check whether it succeeded before submitting changed data.');
			}
			if (!preview) beforePendingSave.current = signature;
			const saved = await api(preview ? `/orders/calculate${order.id ? `?id=${encodeURIComponent(order.id)}` : ''}` :
				order.id ? `/orders/${encodeURIComponent(order.id)}` : '/orders', orderSchema, {
				method: preview || !order.id ? 'POST' : 'PUT', body: signature,
			});
			setOrder(withEntryRow(orderDraft(saved), lineTemplate, canWrite)); setTaxEditor(null); setToleranceEditor(null);
			setCalculationFailure(null);
			if (preview) { setDirty(true); setNotice('Calculated and validated by the server. Not saved.'); }
			else {
				setApprovalRevision(value => value + 1);
				window.dispatchEvent(new Event('poc:tasks-changed'));
				window.dispatchEvent(new Event('poc:sales-orders-changed'));
				const renumbered = !order.id && automaticReference && Boolean(order.header.RecId?.trim()) && order.header.RecId !== saved.header.RecId;
				setDirty(false);
				const reference = saved.header.RecId?.trim();
				toast.success(reference ? `Sales order ${reference} saved` : 'Sales order saved', renumbered ? {
					description: `The provisional reference ${order.header.RecId} changed because another order was saved first.`,
					duration: 10000,
				} : undefined);
				requestKey.current = null; beforePendingSave.current = null;
				if (!id && saved.id) onSaved(saved.id);
			}
		} catch (failure) {
			if (failure instanceof ApiError) {
				const activeItems = order.items.filter((item) => !item.entryOnly);
				const issues = bindFieldErrors(failure.errors, activeItems.map((item) => item.key));
				setFieldErrors(issues); pendingValidationFocus.current = Object.keys(issues).length > 0;
				const expenseItem = activeItems.find((item) => Object.keys(issues).some((path) =>
					path.startsWith(`items[${item.key}].expenses`)));
				if (expenseItem) setTaxEditor({ key: expenseItem.key, expenses: expenseItem.expenses.map((row) => ({ ...row })), dirty: false });
				const toleranceItem = activeItems.find((item) => Object.keys(issues).some((path) =>
					path === `items[${item.key}].Tl_ExcessQty` || path === `items[${item.key}].TL_ShortageQty`));
				if (toleranceItem) setToleranceEditor({ key: toleranceItem.key, dirty: false });
			}
			if (failure instanceof ApiError && ([400, 422].includes(failure.status) ||
				failure.status === 409 && 'header.RecId' in failure.errors)) {
				beforePendingSave.current = null;
				requestKey.current = null;
			}
			setError(errorMessage(failure));
		}
		finally { setBusy(false); }
	}
	async function remove() {
		if (!order?.id || !window.confirm(`Permanently delete order ${order.header.RecId}?`)) return;
		setBusy(true); setError('');
		try {
			await api(`/orders/${encodeURIComponent(order.id)}`, emptySchema, {
				method: 'DELETE', body: JSON.stringify({ version: order.version }),
			});
			toast.success(`Sales order ${order.header.RecId} deleted`);
			setDirty(false); window.dispatchEvent(new Event('poc:tasks-changed'));
			window.dispatchEvent(new Event('poc:sales-orders-changed')); onDeleted();
		} catch (failure) { setError(errorMessage(failure)); setBusy(false); }
	}
	if (!order || !metadata) return <section className="space-y-4">
		<p role={error ? 'alert' : 'status'}>{error || 'Loading Sales order…'}</p>
		{error && <Button onClick={() => setAttempt((value) => value + 1)}>Retry</Button>}
		<Link href="/dashboard/sale-order">Back to orders</Link>
	</section>;
	const disabled = busy || !canWrite;
	const activeItemCount = order.items.filter((item) => !item.entryOnly).length;
	const toleranceFields = metadata.fields.filter((field) => field.section === 'lines' &&
		['tl_excessqty', 'tl_shortageqty'].includes(field.key.toLowerCase()));
	const lineFields = metadata.fields.filter((field) => field.section === 'lines' && !toleranceFields.includes(field) && isVisibleOrderField(field));
	const itemSummary = summarizeOrderItems(order.items);
	const itemWidths = lineFields.map(itemColumnWidth);
	const itemDataWidth = itemWidths.reduce((total, width) => total + width, 80);
	const itemFixedWidth = 32 + 36 + 36 + (toleranceFields.length > 0 ? 36 : 0);
	const itemWidth = (width: number) => `${width / itemDataWidth * 100}%`;
	const taxIndex = order.items.findIndex((item) => item.key === taxEditor?.key);
	const voucherCode = order.header.V_Type?.trim() || 'SORD';
	const previewOnly = !order.id && voucherTypes?.find(voucher => voucher.code === voucherCode)?.canSave === false;
	const toleranceIndex = order.items.findIndex((item) => item.key === toleranceEditor?.key);
	const toleranceItem = order.items[toleranceIndex];
	const actions: RecordAction[] = [
		...(order.id && canWrite ? [{ id: 'edit', label: 'Edit order', icon: Pencil, group: 'document', onSelect: () => {
		const input = workspaceRef.current?.querySelector<HTMLInputElement>('input:not(:disabled)');
		input?.focus(); input?.scrollIntoView({ block: 'center' });
		} }] : []),
		...(order.id && session.capabilities.add ? [{ id: 'copy', label: 'Copy to new order', icon: Copy, group: 'document',
			onSelect: () => router.push(newSalesOrderHref(order.id ?? undefined), { scroll: false }) }] : []),
		...(canWrite && !previewOnly ? [{ id: 'calculate', label: 'Validate / calculate', icon: CheckCheck, group: 'document', onSelect: () => void submit(true) }] : []),
		...(session.capabilities.add ? [{ id: 'new', label: 'New blank order', icon: Plus, group: 'document',
			onSelect: () => router.push(newSalesOrderHref(), { scroll: false }) }] : []),
		...(order.id && session.capabilities.delete ? [{ id: 'delete', label: 'Delete order', icon: Trash2, group: 'danger',
			disabled: order.locked, reason: order.lockReason ?? undefined, destructive: true, onSelect: () => void remove() }] : []),
	];
	const primary: RecordAction | undefined = canWrite && !previewOnly ? {
		id: 'save', label: 'Save order', icon: Save, disabled: !dirty, onSelect: () => void submit(false),
	} : undefined;
	return <div ref={workspaceRef} className="space-y-3">
		<div className="flex flex-wrap justify-between gap-3 items-center">
			<div><Link className="text-xs text-blue-600" href="/dashboard/sale-order">← Sales orders</Link>
				<h1 className="text-xl text-slate-900 font-semibold mt-1">{order.id ? `Order ${order.header.RecId}` : 'New sales order'}</h1>
				<p className="text-[11px] text-slate-400">{dirty ? 'Unsaved changes' : order.id ? 'Persisted record' : 'Server-generated template'} · {voucherCode}</p></div>
		</div>
		{error && <p role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-xs text-red-700">{error}</p>}
		{liveFailure && <p role="alert" className="rounded border border-red-200 bg-red-50 p-2 text-xs text-red-700">{liveFailure.message}</p>}
		{liveSignature && !liveFailure && <p role="status" className="text-[11px] text-slate-500">
			{lastCalculation === liveSignature ? 'Amounts updated by server. Not saved.' : 'Updating amounts…'}
		</p>}
		{notice && <p role="status" className="rounded border border-emerald-200 bg-emerald-50 p-2 text-xs text-emerald-700">{notice}</p>}
		{order.lockReason && <p className="rounded border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800">{order.lockReason}</p>}
		{previewOnly && <p role="status" className="rounded border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800">
			This voucher is preview-only. Amounts can be calculated, but the order cannot be saved.
		</p>}
		<p role="status" className="text-xs text-slate-500">
			Voucher number: <strong>{order.id ? order.header.V_No : order.numbers?.voucherNumber}</strong>
			{' · '}{order.id ? 'Final' : 'Provisional - not reserved until Save'}
			{!order.id && <span>{' · '}{automaticReference ? 'Automatic order reference' : 'Manual order reference'}</span>}
		</p>
		<section className="rounded-lg border border-slate-200 bg-white p-3 space-y-2">
			<h2 className="font-semibold text-xs text-slate-900">Order details</h2>
			<div className="grid grid-cols-2 gap-x-3 gap-y-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6">
				<label className="block space-y-1 text-[11px] text-slate-500">
					<span>Voucher type</span>
					<select className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs text-foreground"
						value={voucherCode} disabled={disabled || Boolean(order.id) || !voucherTypes?.length} onChange={event => void selectVoucher(event.target.value)}>
						{!voucherTypes?.some(voucher => voucher.code === voucherCode) && <option value={voucherCode}>{voucherCode}</option>}
						{voucherTypes?.map(voucher => <option key={voucher.code} value={voucher.code}>
							{voucher.label} ({voucher.code}){voucher.canSave ? '' : ' - Preview only'}
						</option>)}
					</select>
					{!order.id && voucherTypes === null && <span className="block text-amber-800" role="status">
						Voucher selection is unavailable. You can continue with the default voucher.
					</span>}
				</label>
				{metadata.fields.filter((field) => field.section === 'header' && isVisibleOrderField(field)).map((field) =>
					<OrderField key={field.key} field={field} disabled={disabled} value={order.header[field.key] ?? null}
						validationPath={`header.${field.key}`} validationError={displayedErrors[`header.${field.key}`]}
						onChange={(value) => {
							edited(`header.${field.key}`);
							if (field.key === 'RecId') setAutomaticReference(!value?.trim());
							changed((previous) => ({ ...previous, header: { ...previous.header, [field.key]: value } }));
						}} />)}
			</div>
		</section>
		<section className="rounded-lg border border-slate-200 bg-white overflow-hidden">
			<div className="flex justify-between items-center p-3 border-b border-slate-200">
				<div><h2 className="font-semibold text-slate-900">Items <span className="text-slate-400 font-normal">({activeItemCount})</span></h2>
					<p className="text-[11px] text-slate-400">Editing the blank row opens the next row. Untouched entry rows are not saved. Tax applies only to its row.</p></div>
				{canWrite && <Button className="h-8 text-xs" variant="outline" disabled={busy || activeItemCount >= lineLimit} onClick={addLine}><Plus className="size-3" />Add line</Button>}
			</div>
			<GridScrollViewport viewportRef={gridRef} className="max-h-[48vh]" label="Editable order items">
				<table className="erp-grid erp-order-items w-full table-fixed text-xs" style={{ minWidth: itemFixedWidth + itemDataWidth }}>
					<colgroup>
						<col style={{ width: 32 }} />
						{lineFields.map((field, index) => <col key={field.key} style={{ width: itemWidth(itemWidths[index]) }} />)}
						<col style={{ width: itemWidth(80) }} />
						{toleranceFields.length > 0 && <col style={{ width: 36 }} />}
						<col style={{ width: 36 }} /><col style={{ width: 36 }} />
					</colgroup>
					<thead className="text-left text-slate-500 bg-slate-50 sticky top-0 z-10">
					<tr><th scope="col" className="sticky left-0 bg-slate-50 z-20">#</th>
						{lineFields.map((field) => <th scope="col" key={field.key}
							className={field.key === 'ItemCode' ? 'sticky left-8 bg-slate-50 z-20' : undefined}>
							{fieldLabel(field)}{field.required && <span className="text-red-500"> *</span>}
						</th>)}
						<th scope="col" className="text-right">Amount</th>
						{toleranceFields.length > 0 && <th scope="col" title="Quantity tolerance"><SlidersHorizontal aria-hidden="true" className="mx-auto size-3.5" /><span className="sr-only">Tolerance</span></th>}
						<th scope="col" className="sticky right-9 z-20 bg-slate-50 border-l border-slate-200">Tax</th>
						<th scope="col" className="sticky right-0 z-20 bg-slate-50"><span className="sr-only">Actions</span></th>
					</tr>
				</thead><tbody>{order.items.map((item, index) => <tr key={item.key} data-entry-row={item.entryOnly ? 'true' : undefined} aria-label={`Item row ${index + 1}`}>
					<td className="sticky left-0 bg-white z-5 text-center text-slate-400">{index + 1}</td>
					{lineFields.map((field) => <td key={field.key} className={field.key === 'ItemCode' ? 'sticky left-8 bg-white z-5' : undefined}>
						<OrderField field={field} value={item.values[field.key] ?? null} disabled={disabled} rowNumber={index + 1}
							validationPath={`items[${item.key}].${field.key}`} validationError={displayedErrors[`items[${item.key}].${field.key}`]}
							onChange={(value) => {
								edited(`items[${item.key}].${field.key}`);
								if (!lineTemplate) return;
								if (field.key === 'ItemCode') {
									if (value !== (item.values.ItemCode ?? null)) void changeItem(item.key, value);
									return;
								}
								changed((previous) => ({ ...previous, items: editEntryRow(previous.items, item.key,
									(row) => ({ ...row, values: { ...row.values, [field.key]: value } }),
									() => itemDraft(lineTemplate.values, lineTemplate.expenses), lineLimit) }));
							}} />
					</td>)}
					<td className="truncate text-right tabular-nums" title={item.values.Amount ?? undefined}>{item.values.Amount ?? '—'}</td>
					{toleranceFields.length > 0 && <td><Button variant="ghost" className="size-8 p-0 text-blue-600"
						disabled={busy || item.entryOnly || Boolean(liveSignature && lastCalculation !== liveSignature && !liveFailure)}
						aria-label={`Quantity tolerance for row ${index + 1}`}
						title={canWrite ? 'Edit quantity tolerance' : 'View quantity tolerance'}
						aria-invalid={toleranceFields.some(field => Boolean(displayedErrors[`items[${item.key}].${field.key}`]))}
						onClick={() => setToleranceEditor({ key: item.key, dirty: false })}>
						<SlidersHorizontal className="size-3.5" />
					</Button></td>}
					<td className="sticky right-9 z-5 bg-white border-l border-slate-200"><Button variant="ghost" className="size-8 p-0 text-blue-600" disabled={busy} aria-label={`Tax and expenses for row ${index + 1}`}
						title={canWrite ? 'Edit tax and expenses' : 'View tax and expenses'}
						onClick={(event) => { taxTrigger.current = event.currentTarget; setTaxEditor({ key: item.key, expenses: item.expenses.map((row) => ({ ...row })), dirty: false }); }}>
						<ReceiptText className="size-3.5" />
					</Button></td>
					<td className="sticky right-0 z-5 bg-white">{canWrite && <Button variant="ghost" className="size-7 text-slate-400 hover:text-red-600" aria-label={`Remove row ${index + 1}`}
						onClick={() => changed((previous) => withEntryRow({ ...previous, items: previous.items.filter((row) => row.key !== item.key) }, lineTemplate, canWrite))}
						disabled={busy || item.entryOnly}><Trash2 className="size-3.5" /></Button>}</td>
				</tr>)}</tbody></table>
				{!order.items.length && <p className="p-6 text-center text-slate-400">No items yet. Add a line to begin.</p>}
			</GridScrollViewport>
			<div className="border-t border-slate-200 bg-slate-50 px-3 py-2" aria-label="Item totals">
				<dl className="flex flex-wrap items-center gap-x-8 gap-y-2 text-xs">
					<div className="flex items-baseline gap-2"><dt className="text-slate-500">Total quantity (reels)</dt>
						<dd className="font-semibold tabular-nums text-slate-900">{itemSummary.quantity ?? 'Check values'}</dd></div>
					<div className="flex items-baseline gap-2"><dt className="text-slate-500">Total weight</dt>
						<dd className="font-semibold tabular-nums text-slate-900">{itemSummary.weight ?? 'Check values'}</dd></div>
				</dl>
				{(itemSummary.quantity === null || itemSummary.weight === null) &&
					<p role="alert" className="mt-1 text-xs text-red-700">Correct invalid or negative quantity/weight values to show their totals.</p>}
			</div>
		</section>
		<div className="grid gap-3 xl:grid-cols-2">
			<section className="min-w-0 rounded-lg border border-slate-200 bg-white p-3 space-y-2"><h2 className="font-semibold text-xs">Expenses and totals</h2>
				<ExpenseEditor label="Order expenses and totals" rows={order.headerExpenses} disabled={disabled} path="headerExpenses" errors={displayedErrors} onEdit={edited}
					onChange={(rows) => changed({ ...order, headerExpenses: rows })} />
				<p className="text-[10px] text-amber-700">Legacy SORD: Bill / Net can remain zero. Gross Amount is the primary total.</p>
			</section>
			<VoucherTermsEditor terms={order.terms} disabled={disabled} errors={fieldErrors} onChange={(code, values, path) => {
				edited(path);
				changed((draft) => ({ ...draft, terms: draft.terms.map((term) => term.Code === code ? { ...term, ...values } : term) }));
			}} />
		</div>
		<OrderApproval active={active} key={`${order.id}:${order.version}:${approvalRevision}`} id={order.id ?? undefined} version={order.version ?? undefined}
			dirty={dirty || Boolean(taxEditor?.dirty) || Boolean(toleranceEditor?.dirty)} busy={busy} canApprove={session.capabilities.approve} onBusy={setBusy}
			onChanged={() => { setOrder(null); setAttempt(value => value + 1); }} actions={actions} primary={primary}
			status={dirty ? 'Unsaved changes' : order.id ? 'Saved record' : 'New draft'}
			leading={<div role="group" aria-label="Document actions" className="flex flex-wrap items-center gap-1">
				<Button type="button" size="sm" variant="ghost" className="h-8 text-xs" disabled>
					<Paperclip className="size-3.5" />Attachments
				</Button>
				{active && <OrderPdfPreview key={JSON.stringify([session.contextVersion, order.id, order.version])}
					id={order.canPrintPdf && session.capabilities.print ? order.id : null}
					disabled={busy || hasChanges} />}
				{order.canPrintPdf && <span className="px-1 text-[11px] text-slate-500">Crystal sample</span>}
			</div>}
			details={order.id ? [
				{ label: 'Created by', value: order.recordInfo?.createdBy }, { label: 'Modified by', value: order.recordInfo?.modifiedBy },
				{ label: 'Approved by', value: order.recordInfo?.approvedBy },
			] : []} />
		<Dialog open={active && taxEditor !== null} onOpenChange={(open) => { if (!open) setTaxEditor(null); }}>
			<DialogContent ref={taxContentRef} className="sm:max-w-2xl" onCloseAutoFocus={(event) => { event.preventDefault(); taxTrigger.current?.focus(); }}>
				<DialogHeader><DialogTitle>Tax and expenses · Row {taxIndex + 1}</DialogTitle>
					<DialogDescription>{order.items[taxIndex]?.values.ItemCode ?? 'Item'} · Changes apply only to this item row. Save order commits all rows together.</DialogDescription></DialogHeader>
				<div className="max-h-[55vh] overflow-auto">
					<ExpenseEditor label="Item tax and expenses" rows={taxEditor?.expenses ?? []} calculatedRows={taxPreview?.rows}
						disabled={disabled} errors={{ ...fieldErrors, ...taxPreview?.errors }}
						path={`items[${taxEditor?.key}].expenses`}
						onChange={(expenses) => setTaxEditor((editor) => editor ? { ...editor,
							expenses: retainCalculatedExpenseAmounts(editor.expenses, expenses, taxPreview?.rows), dirty: true } : null)} />
				</div>
				{taxPreview?.message ? <p role="alert" className="text-xs text-red-700">{taxPreview.message}</p> :
					<p role="status" className="text-xs text-slate-500">{taxSignature && !taxPreview
						? 'Recalculating expenses…' : 'Apply updates the draft. Save commits the order.'}</p>}
				<DialogFooter><Button variant="outline" onClick={() => setTaxEditor(null)}>{canWrite ? 'Cancel' : 'Close'}</Button>
					{canWrite && <Button variant="outline"
						disabled={busy || taxIndex < 0 || !order.items.some(item => item.key !== taxEditor?.key && !item.entryOnly) ||
							Boolean(taxEditor?.dirty && !taxPreview?.rows)} onClick={() => {
							if (!taxEditor || !window.confirm('Apply matching expense rates to all other order lines?')) return;
							const rows = taxPreview?.rows ?? taxEditor.expenses;
							setFieldErrors(previous => Object.fromEntries(Object.entries(previous)
								.filter(([path]) => !/^items\[[^\]]+\]\.expenses/.test(path))));
							changed(previous => withEntryRow(
								applyItemExpenseRatesToAll(previous, taxEditor.key, rows), lineTemplate, canWrite));
							setTaxEditor(null);
						}}>Apply rates to all</Button>}
					{canWrite && <Button className="bg-blue-600 hover:bg-blue-700"
						disabled={busy || taxIndex < 0 || Boolean(taxEditor?.dirty && !taxPreview?.rows)} onClick={() => {
						if (!taxEditor) return;
						if (taxEditor.dirty && taxPreview?.rows) {
							const rows = taxPreview.rows;
							setFieldErrors((previous) => Object.fromEntries(Object.entries(previous)
								.filter(([path]) => !path.startsWith(`items[${taxEditor.key}].expenses`))));
							changed((previous) => withEntryRow(
								applyItemExpenses(previous, taxEditor.key, rows), lineTemplate, canWrite));
						}
						setTaxEditor(null);
					}}>Apply to row</Button>}</DialogFooter>
			</DialogContent>
		</Dialog>
		{toleranceEditor && toleranceItem && <QuantityToleranceEditor key={toleranceEditor.key} active={active}
			calculate={calculateTolerance}
			contentRef={toleranceContentRef} paths={{ excess: `items[${toleranceEditor.key}].Tl_ExcessQty`,
				shortage: `items[${toleranceEditor.key}].TL_ShortageQty` }}
			rowNumber={toleranceIndex + 1} quantity={toleranceItem.values.Qty ?? '0'}
			excess={toleranceItem.values.Tl_ExcessQty ?? '0'} shortage={toleranceItem.values.TL_ShortageQty ?? '0'}
			disabled={disabled || toleranceFields.some(field => !field.writable)}
			onDirty={() => setToleranceEditor((editor) => editor ? { ...editor, dirty: true } : null)}
			onClose={() => setToleranceEditor(null)}
			onApply={(excess, shortage) => {
				edited(`items[${toleranceEditor.key}].Tl_ExcessQty`); edited(`items[${toleranceEditor.key}].TL_ShortageQty`);
				changed((previous) => ({ ...previous, items: previous.items.map(item => item.key === toleranceEditor.key ?
					{ ...item, values: { ...item.values, Tl_ExcessQty: excess, TL_ShortageQty: shortage } } : item) }));
				setToleranceEditor(null);
			}} />}
	</div>;
}
