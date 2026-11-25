export function scheduleCalculation<T>(signature: string, load: (signature: string, signal: AbortSignal) => Promise<T>,
	onResult: (result: T, signature: string) => void, onError: (error: unknown, signature: string) => void, delay = 400) {
	const controller = new AbortController();
	const timer = setTimeout(() => {
		load(signature, controller.signal).then((result) => {
			if (!controller.signal.aborted) onResult(result, signature);
		}).catch((error: unknown) => {
			if (!controller.signal.aborted) onError(error, signature);
		});
	}, delay);
	return () => { clearTimeout(timer); controller.abort(); };
}
