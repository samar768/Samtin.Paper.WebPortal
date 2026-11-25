import { z } from 'zod';

export const erpTaskSchema = z.object({
	id: z.string().uuid(), type: z.string(), category: z.enum(['Memo', 'Note', 'Notification', 'Approval']),
	title: z.string(), body: z.string(), state: z.enum(['Open', 'Completed', 'Rejected', 'Cancelled']),
	createdAt: z.string().datetime({ offset: true }), createdBy: z.string(), read: z.boolean(),
	href: z.string().regex(/^\/dashboard\/sale-order\/[^/?#]+$/).nullable(),
});
export type ErpTask = z.infer<typeof erpTaskSchema>;
export const taskInboxSchema = z.object({ items: z.array(erpTaskSchema).max(25), unread: z.number().int().nonnegative(), hasMore: z.boolean() });
export const taskTypesSchema = z.array(z.object({ code: z.string(), name: z.string() }));
export const approvalStateSchema = z.object({
	requestId: z.string().uuid().nullable(), state: z.enum(['NotRequested', 'Pending', 'Approved', 'Rejected', 'Cancelled', 'Reversed']),
	version: z.string().nullable(), canAct: z.boolean(),
	canReverse: z.boolean().default(false), reversedBy: z.string().nullable().default(null), reversalReason: z.string().nullable().default(null),
	stages: z.array(z.object({
		step: z.number().int().positive(), name: z.string(), group: z.string(),
		state: z.enum(['Waiting', 'Active', 'Approved', 'Rejected', 'Cancelled']), actor: z.string().nullable(),
		comment: z.string().nullable(), actedAt: z.string().datetime({ offset: true }).nullable(),
	})),
});
export type ApprovalState = z.infer<typeof approvalStateSchema>;
