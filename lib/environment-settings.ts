import { z } from 'zod';
import { masterFormSchema } from './erp-masters';

export const environmentTabs = ['Gate', 'Sales', 'Excise', 'Purchase', 'CostSheet', 'Stores'] as const;
export const environmentFormSchema = masterFormSchema.extend({
	layout: z.array(z.object({
		key: z.string(), controlName: z.string(), tab: z.enum(environmentTabs), group: z.string(),
		lookupId: z.string().nullable(), multiple: z.boolean(), integerDigits: z.number().nullable(),
		fractionDigits: z.number().nullable(), maximum: z.number().nullable(),
		choices: z.array(z.object({ code: z.string(), label: z.string(), selectable: z.boolean() })),
	})),
}).superRefine((form, context) => {
	const fields = form.definition.fields.map(field => field.key);
	if (form.layout.length !== fields.length || new Set(form.layout.map(field => field.key)).size !== fields.length ||
		form.layout.some(field => !fields.includes(field.key) || field.multiple && !field.lookupId))
		context.addIssue({ code: 'custom', message: 'Environment field and tab configuration do not match.' });
});
export type EnvironmentForm = z.infer<typeof environmentFormSchema>;
