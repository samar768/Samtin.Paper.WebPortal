import { z } from 'zod';

export const rowSchema = z.record(z.string(), z.string().nullable());
export type DataRow = z.infer<typeof rowSchema>;
const companyInformationSchema = z.object({
	registeredName: z.string(), displayName: z.string(), address1: z.string(), address2: z.string(), city: z.string(),
	country: z.string(), pin: z.string(), phone: z.string(), email: z.string(), pan: z.string(), tin: z.string(), headOffice: z.boolean(),
});
export const sessionSchema = z.object({
	username: z.string(), company: z.string(), companyName: z.string(), site: z.string(),
	financialYear: z.string(), prefix: z.string(), startDate: z.string(), endDate: z.string(),
	capabilities: z.object({
		view: z.boolean(), add: z.boolean(), edit: z.boolean(), delete: z.boolean(),
		print: z.boolean(), selectedPriceList: z.boolean(), metadataAdministration: z.boolean(), approve: z.boolean().default(false),
	}),
	siteName: z.string(), contextVersion: z.string().length(32), readOnly: z.boolean(), companyInformation: companyInformationSchema,
});
export type Session = z.infer<typeof sessionSchema>;
export const authenticationSchema = z.object({ username: z.string(), context: sessionSchema.nullable() });
export const companySelectionSchema = z.object({
	username: z.string(), company: z.string().nullable(), site: z.string().nullable(), contextVersion: z.string().nullable(),
	companies: z.array(z.object({
		company: z.string(), companyName: z.string(), financialYear: z.string(), startDate: z.string(), endDate: z.string(),
		sites: z.array(z.object({ code: z.string(), name: z.string(), headOffice: z.boolean() })),
		available: z.boolean(), unavailableReason: z.string().nullable(), readOnly: z.boolean(),
	})),
});
let activeContextVersion: string | null = null;
export function setApiContext(version: string | null) { activeContextVersion = version; }
const navigationItemSchema = z.object({
	key: z.string(), label: z.string(), parentKey: z.string().nullable(),
	order: z.number(), href: z.string(), implemented: z.boolean(),
});
export const navigationSchema = z.object({ warnings: z.array(z.string()), modules: z.array(z.object({
	key: z.string(), label: z.string(), items: z.array(navigationItemSchema),
})) });
export type NavigationModule = z.infer<typeof navigationSchema>['modules'][number];
export type NavigationItem = z.infer<typeof navigationItemSchema>;
export const fieldSchema = z.object({
	key: z.string(), section: z.string(), label: z.string(), type: z.string(),
	required: z.boolean(), length: z.number(), precision: z.number(), scale: z.number(),
	lookup: z.string().nullable(), writable: z.boolean(),
	valueHelp: z.string().nullable().optional(),
});
export type Field = z.infer<typeof fieldSchema>;
export const metadataSchema = z.object({ version: z.string(), fields: z.array(fieldSchema) });
export type OrderMetadata = z.infer<typeof metadataSchema>;
export const orderSchema = z.object({
	id: z.string().nullable(), version: z.string().nullable(), metadataVersion: z.string(),
	header: rowSchema, lines: z.array(rowSchema), headerExpenses: z.array(rowSchema),
	lineExpenses: z.array(z.array(rowSchema)), terms: z.array(rowSchema),
	locked: z.boolean(), lockReason: z.string().nullable(),
	numbers: z.object({ reference: z.string(), voucherNumber: z.string() }).nullable().optional(),
	canPrintPdf: z.boolean().optional(),
	recordInfo: z.object({
		createdBy: z.string().nullable(), modifiedBy: z.string().nullable(), approvedBy: z.string().nullable(),
		changeBlockedReason: z.string().nullable(),
	}).nullable().optional(),
});
export type Order = z.infer<typeof orderSchema>;
export const rowsSchema = z.array(rowSchema);
export const emptySchema = z.undefined();
const orderVoucherTypesSchema = z.array(z.object({ code: z.string(), label: z.string(), canSave: z.boolean() }));
export type OrderVoucherType = z.infer<typeof orderVoucherTypesSchema>[number];

export async function loadOrderVoucherTypes(signal?: AbortSignal): Promise<OrderVoucherType[] | null> {
	try {
		return await api('/orders/voucher-types', orderVoucherTypesSchema, { signal });
	} catch (failure) {
		if (failure instanceof ApiError && failure.status === 404) return null;
		throw failure;
	}
}

export class ApiError extends Error {
	readonly status: number;
	readonly traceId?: string;
	readonly errors: Record<string, string[]>;
	constructor(status: number, message: string, traceId?: string, errors: Record<string, string[]> = {}) {
		super(message);
		this.status = status;
		this.traceId = traceId;
		this.errors = errors;
	}
}

async function request(path: string, init: RequestInit = {}): Promise<Response> {
	const headers = new Headers(init.headers);
	if (activeContextVersion && !headers.has('X-ERP-Context')) headers.set('X-ERP-Context', activeContextVersion);
	if (init.body) headers.set('Content-Type', 'application/json');
	if (init.method && !['GET', 'HEAD'].includes(init.method)) {
		const tokenResponse = await fetch('/api/poc/csrf', { cache: 'no-store', signal: init.signal });
		if (!tokenResponse.ok) throw new ApiError(tokenResponse.status, 'Unable to obtain a request token.');
		const token = z.object({ token: z.string() }).parse(await tokenResponse.json());
		headers.set('X-CSRF-TOKEN', token.token);
	}
	const response = await fetch(`/api/poc${path}`, { ...init, headers, cache: 'no-store' });
	if (!response.ok) {
		const problem = z.object({ detail: z.string().optional(), title: z.string().optional(), traceId: z.string().optional(),
			errors: z.record(z.string(), z.array(z.string())).optional() })
			.safeParse(await response.json().catch(() => null));
		if (response.status === 401 && typeof window !== 'undefined' && path !== '/session') {
			window.dispatchEvent(new Event('poc:session-expired'));
		}
		if (response.status === 403 && typeof window !== 'undefined' && path !== '/session') {
			window.dispatchEvent(new Event('poc:grants-changed'));
		}
		if (response.status === 409 && typeof window !== 'undefined' && problem.success &&
			['context_changed', 'context_required'].includes(problem.data.title ?? '')) {
			window.dispatchEvent(new Event('poc:context-changed'));
		}
		throw new ApiError(response.status,
			problem.success && problem.data.detail ? problem.data.detail : `Request failed (${response.status}).`,
			problem.success ? problem.data.traceId : undefined, problem.success ? problem.data.errors : undefined);
	}
	return response;
}

export async function fetchOrderPdf(id: string, signal?: AbortSignal): Promise<Blob> {
	const response = await request(`/orders/${encodeURIComponent(id)}/print.pdf`, { signal });
	if (response.headers.get('Content-Type')?.split(';')[0] !== 'application/pdf') {
		throw new Error('The server did not return a PDF.');
	}
	return response.blob();
}

export async function api<T>(path: string, schema: z.ZodType<T>, init: RequestInit = {}): Promise<T> {
	const response = await request(path, init);
	const parsed = schema.safeParse(response.status === 204 ? undefined : await response.json());
	if (!parsed.success) {
		console.error('Unexpected POC API contract', { path });
		throw new Error('The server returned an unexpected response. Contact the administrator.');
	}
	return parsed.data;
}

export function errorMessage(error: unknown): string {
	if (error instanceof ApiError) return `${error.message}${error.traceId ? ` (Reference: ${error.traceId})` : ''}`;
	return error instanceof Error ? error.message : 'The operation failed. Please retry.';
}

export function savePayload(order: Order, metadata: OrderMetadata, idempotencyKey: string, automaticReference = false) {
	const project = (row: DataRow, section: string): DataRow => Object.fromEntries(
		metadata.fields.filter((field) => field.section === section && field.writable)
			.filter((field) => field.key in row).map((field) => [field.key, row[field.key]]),
	);
	const expenses = (rows: DataRow[]) => rows.map((row) => ({
		SNo: row.SNo,
		...(row.PerEditable === 'Y' ? { Per: row.Per } : {}),
		...(row.AmtEditable === 'Y' ? { Amount: row.Amount } : {}),
	}));
	return {
		metadataVersion: order.metadataVersion, version: order.version, idempotencyKey, automaticReference,
		...(order.header.V_Type && order.header.V_Type !== 'SORD' ? { voucherType: order.header.V_Type } : {}),
		header: project(order.header, 'header'),
		lines: order.lines.map((line) => ({
			...project(line, 'lines'),
			...(order.id && Number(line.V_SNo) > 0 ? { V_SNo: line.V_SNo } : {}),
		})),
		headerExpenses: expenses(order.headerExpenses),
		lineExpenses: order.lines.map((_, index) => expenses(order.lineExpenses[index] ?? [])),
		terms: order.terms.map((row) => row.Editor === 'lookup' || row.LookupId
			? { Code: row.Code, ValueCode: row.ValueCode ?? null }
			: { Code: row.Code, Value: row.Value }),
	};
}
