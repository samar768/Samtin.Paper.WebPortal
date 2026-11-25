import type { DataRow } from './poc-api';

export function masterAuditChanges(before: DataRow | null, after: DataRow | null) {
	return [...new Set([...Object.keys(before ?? {}), ...Object.keys(after ?? {})])]
		.map(key => ({ key, before: before?.[key] ?? null, after: after?.[key] ?? null }))
		.filter(change => change.before !== change.after);
}
