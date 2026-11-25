'use client';

import { createContext, useContext, useEffect, useState } from 'react';
import { useParams, usePathname } from 'next/navigation';
import { Boxes, ChartNoAxesCombined, CircleDollarSign, ClipboardList, Cog, ContactRound,
	Factory, FileChartColumn, FolderKanban, Package, ScanBarcode, ShoppingBag, Truck, UsersRound,
	type LucideIcon } from 'lucide-react';
import { api, errorMessage, navigationSchema, type NavigationModule } from '@/lib/poc-api';
import { useSession } from '@/components/poc-session';
import { Button } from '@/components/ui/button';

const icons: Record<string, LucideIcon> = {
	sales: ShoppingBag, purchase: Truck, stores: Package, accounts: CircleDollarSign,
	master: Boxes, utility: Cog, report: FileChartColumn, formmodule: ClipboardList,
	payroll: UsersRound, crm: ContactRound, machine: Factory, barcode: ScanBarcode,
};

export function ModuleIcon({ module, className = 'size-4' }: { module: string; className?: string }) {
	const Icon = icons[module.toLowerCase()] ?? FolderKanban;
	return <Icon className={className} aria-hidden="true" />;
}

const NavigationContext = createContext<{
	modules: NavigationModule[]; current: NavigationModule | undefined; warnings: string[];
} | null>(null);

export function useNavigation() {
	const value = useContext(NavigationContext);
	if (!value) throw new Error('Module navigation has not loaded.');
	return value;
}

export function NavigationProvider({ children }: { children: React.ReactNode }) {
	const session = useSession();
	const pathname = usePathname();
	const params = useParams<{ module?: string }>();
	const [modules, setModules] = useState<NavigationModule[] | null>(null);
	const [warnings, setWarnings] = useState<string[]>([]);
	const [error, setError] = useState('');
	const [attempt, setAttempt] = useState(0);
	useEffect(() => {
		const controller = new AbortController();
		api('/navigation', navigationSchema, { signal: controller.signal }).then((result) => {
			setModules(result.modules); setWarnings(result.warnings); setError('');
		}).catch((failure: unknown) => { if (!controller.signal.aborted) { setModules(null); setError(errorMessage(failure)); } });
		return () => controller.abort();
	}, [session, attempt]);
	if (!modules) return <main className="min-h-screen grid place-content-center bg-slate-50 p-6 text-slate-700">
		<p role={error ? 'alert' : 'status'}>{error || 'Loading your authorized modules...'}</p>
		{error && <Button className="mt-3" onClick={() => setAttempt((value) => value + 1)}>Retry navigation</Button>}
	</main>;
	const current = params.module ? modules.find((module) => module.key === params.module || encodeURIComponent(module.key) === params.module)
		: modules.find((module) => module.items.some((item) => item.implemented && pathname.startsWith(item.href)))
			?? modules.find((module) => module.key.toLowerCase() === 'sales') ?? modules[0];
	return <NavigationContext.Provider value={{ modules, current, warnings }}>{children}</NavigationContext.Provider>;
}

export function ModuleMark() {
	return <ChartNoAxesCombined className="size-5 text-blue-600" aria-hidden="true" />;
}
