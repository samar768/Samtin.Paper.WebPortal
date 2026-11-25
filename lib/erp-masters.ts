import { z } from 'zod';
import { fieldSchema, rowSchema } from './poc-api';

export const masterDefinitionSchema = z.object({
	id: z.string(), title: z.string(), revision: z.number().int().positive(), version: z.string(),
	fields: z.array(fieldSchema).min(1), defaults: rowSchema, lookupIds: z.array(z.string()),
	scope: z.enum(['global', 'site']), captureAuditValues: z.boolean(),
	capabilities: z.object({ view: z.boolean(), add: z.boolean(), edit: z.boolean(), delete: z.boolean(), print: z.boolean(), audit: z.boolean() }),
});
export const masterRecordSchema = z.object({
	id: z.string(), version: z.string(), metadataVersion: z.string(), values: rowSchema,
	recordInfo: z.object({
		createdBy: z.string().nullable(), modifiedBy: z.string().nullable(), approvedBy: z.string().nullable(),
		changeBlockedReason: z.string().nullable(),
	}),
});
export const masterFormSchema = z.object({ definition: masterDefinitionSchema, record: masterRecordSchema.nullable() });
export const masterListSchema = z.object({
	definition: masterDefinitionSchema, records: z.array(masterRecordSchema), page: z.number().int(), hasMore: z.boolean(),
});
export const masterPrintSchema = z.object({ definition: masterDefinitionSchema, records: z.array(masterRecordSchema) });
export const masterAuditSchema = z.object({
	definition: masterDefinitionSchema, page: z.number().int(), hasMore: z.boolean(),
	entries: z.array(z.object({
		id: z.string(), recordId: z.string(), username: z.string(), operation: z.enum(['create', 'edit', 'delete']),
		occurredAt: z.string(), companyCode: z.string().nullable(), siteCode: z.string().nullable(),
		metadataRevision: z.number().int().nullable(), valuesCaptured: z.boolean(),
		before: rowSchema.nullable(), after: rowSchema.nullable(),
	})),
});
export type MasterDefinition = z.infer<typeof masterDefinitionSchema>;
export type MasterRecord = z.infer<typeof masterRecordSchema>;
export type MasterForm = z.infer<typeof masterFormSchema>;
export type MasterList = z.infer<typeof masterListSchema>;
export type MasterPrint = z.infer<typeof masterPrintSchema>;
export type MasterAudit = z.infer<typeof masterAuditSchema>;

