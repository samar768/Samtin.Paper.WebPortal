'use client';

import { useEffect } from 'react';
import { scheduleCalculation } from './debounced-calculation';

export function useDebouncedCalculation<T>(signature: string | null, load: (signature: string, signal: AbortSignal) => Promise<T>,
	onResult: (result: T, signature: string) => void, onError: (error: unknown, signature: string) => void) {
	useEffect(() => {
		if (signature === null) return;
		return scheduleCalculation(signature, load, onResult, onError);
	}, [signature, load, onResult, onError]);
}
