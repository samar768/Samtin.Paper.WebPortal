import { z } from 'zod';

export type ListingColumnDefinition = { id: string; label: string; width: number };
export type ListingDefinition = { key: string; columns: readonly ListingColumnDefinition[]; sortColumn: string };

export function createListingPreferencesSchema(columns: readonly ListingColumnDefinition[]) {
	const ids = columns.map(column => column.id);
	return z.object({
		schemaVersion: z.literal(1),
		columns: z.array(z.object({ id: z.string(), visible: z.boolean(), width: z.number().int()
			.min(80, 'Column widths must be between 80 and 600 pixels.')
			.max(600, 'Column widths must be between 80 and 600 pixels.') })).length(ids.length),
		density: z.enum(['compact', 'comfortable']),
		pageSize: z.union([z.literal(10), z.literal(25), z.literal(50), z.literal(100)]),
		sortColumn: z.string(),
		sortDirection: z.enum(['asc', 'desc']),
	}).superRefine((value, context) => {
		if (new Set(value.columns.map(column => column.id)).size !== ids.length ||
			value.columns.some(column => !ids.includes(column.id)) || !ids.includes(value.sortColumn)) {
			context.addIssue({ code: 'custom', message: 'Choose unique available columns and an available sort column.' });
		}
		if (!value.columns.some(column => column.visible)) {
			context.addIssue({ code: 'custom', message: 'Keep at least one column visible.' });
		}
	});
}

export type ListingPreferences = z.infer<ReturnType<typeof createListingPreferencesSchema>>;

export function defaultListingPreferences(definition: ListingDefinition): ListingPreferences {
	return {
		schemaVersion: 1, columns: definition.columns.map(column => ({ id: column.id, visible: true, width: column.width })),
		density: 'compact', pageSize: 25, sortColumn: definition.sortColumn, sortDirection: 'desc',
	};
}

export function moveListingColumn(preferences: ListingPreferences, id: string, offset: -1 | 1): ListingPreferences {
	const index = preferences.columns.findIndex(column => column.id === id);
	if (index === -1) throw new Error('The listing column no longer exists.');
	const target = index + offset;
	if (target < 0 || target >= preferences.columns.length) return preferences;
	const columns = [...preferences.columns];
	[columns[index], columns[target]] = [columns[target], columns[index]];
	return { ...preferences, columns };
}

export function visibleListingColumns(preferences: ListingPreferences) {
	return preferences.columns.filter(column => column.visible);
}
