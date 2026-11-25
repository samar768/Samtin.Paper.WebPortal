'use client';

import { useEffect, useMemo, useState } from 'react';
import { toast } from 'sonner';
import { api, emptySchema, errorMessage } from '@/lib/poc-api';
import { createListingPreferencesSchema, defaultListingPreferences, type ListingDefinition, type ListingPreferences } from '@/lib/listing-preferences';

export function useListingPreferences(definition: ListingDefinition) {
	const schema = useMemo(() => createListingPreferencesSchema(definition.columns), [definition]);
	const path = `/preferences/listings/${encodeURIComponent(definition.key)}`;
	const [preferences, setPreferences] = useState<ListingPreferences | null>(null);
	const [error, setError] = useState('');
	const [attempt, setAttempt] = useState(0);
	useEffect(() => {
		const controller = new AbortController();
		api(path, schema, { signal: controller.signal }).then(value => {
			setPreferences(value); setError('');
		}).catch((failure: unknown) => {
			if (!controller.signal.aborted) setError(errorMessage(failure));
		});
		return () => controller.abort();
	}, [path, schema, attempt]);

	return {
		preferences, error,
		retry: () => { setError(''); setAttempt(value => value + 1); },
		save: async (value: ListingPreferences) => {
			const saved = await api(path, schema, { method: 'PUT', body: JSON.stringify(schema.parse(value)) });
			setPreferences(saved); setError('');
			toast.success('Listing settings saved');
		},
		reset: async () => {
			await api(path, emptySchema, { method: 'DELETE' });
			setPreferences(defaultListingPreferences(definition)); setError('');
			toast.success('Default listing settings restored');
		},
	};
}
