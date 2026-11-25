import type { DataRow } from './poc-api';
import type { MasterForm } from './erp-masters';

export function masterSavePayload(form: MasterForm, values: DataRow, idempotencyKey: string) {
	return {
		metadataVersion: form.definition.version, version: form.record?.version ?? null, idempotencyKey,
		values: Object.fromEntries(form.definition.fields.filter(field => field.writable)
			.map(field => [field.key, values[field.key] ?? null])),
	};
}
