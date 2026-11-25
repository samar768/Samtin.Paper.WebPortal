import { z } from 'zod';
import { api, rowSchema } from './poc-api';
import { createLookupResolver } from './lookup-resolver';

export const lookupDefinitionSchema = z.object({
	id: z.string(), title: z.string(), version: z.string(), pageSize: z.number().int().min(10).max(50),
	columns: z.array(z.object({ key: z.string(), label: z.string(), width: z.number().int().min(40).max(400) })).min(1),
	create: z.object({ masterId: z.string(), title: z.string() }).nullable().optional(),
});
export const lookupOptionSchema = z.object({ key: z.string(), label: z.string(), cells: rowSchema });
export const lookupSearchSchema = z.object({
	definition: lookupDefinitionSchema, options: z.array(lookupOptionSchema), page: z.number().int(), hasMore: z.boolean(),
});
export const lookupResolveSchema = z.object({
	definition: lookupDefinitionSchema, options: z.array(lookupOptionSchema), missingKeys: z.array(z.string()),
});
export type LookupDefinition = z.infer<typeof lookupDefinitionSchema>;
export type LookupOption = z.infer<typeof lookupOptionSchema>;
export type LookupSearch = z.infer<typeof lookupSearchSchema>;

export const lookupResolver = createLookupResolver<LookupOption>(async (scope, lookup, keys) => {
	const { contextVersion } = z.object({ username: z.string(), contextVersion: z.string() }).parse(JSON.parse(scope));
	const response = await api(`/value-help/${encodeURIComponent(lookup)}/resolve`, lookupResolveSchema, {
		method: 'POST', headers: { 'X-ERP-Context': contextVersion }, body: JSON.stringify({ keys }),
	});
	if (response.missingKeys.some(key => !keys.includes(key)) ||
		response.options.length + response.missingKeys.length !== keys.length ||
		new Set([...response.options.map(option => option.key), ...response.missingKeys]).size !== keys.length)
		throw new Error('The lookup returned inconsistent reference resolution. Contact the administrator.');
	return response.options;
});
