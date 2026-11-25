'use client';

import Link from 'next/link';
import { ArrowUpRight } from 'lucide-react';
import { ModuleIcon, useNavigation } from '@/components/erp-navigation';
import { useSession } from '@/components/poc-session';
import { TaskInbox } from '@/components/erp-tasks';

export default function DashboardPage() {
	const session = useSession();
	const { modules } = useNavigation();
	return <section className="space-y-5">
		<div><p className="text-xs text-slate-400 mb-1">YOUR WORKSPACE</p><h1 className="text-xl font-semibold text-slate-900">Welcome, {session.username}</h1>
			<p className="text-sm text-slate-500 mt-1">Choose a module to view your authorized ERP menus.</p></div>
		<div className="rounded-lg border border-blue-100 bg-blue-50 px-4 py-3 text-xs text-blue-800">
			Sales Order is available in this web POC. Other authorized menus are visible for navigation review and clearly marked when not yet implemented.
		</div>
		<div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
			{modules.map((module) => <Link key={module.key} href={`/dashboard/modules/${encodeURIComponent(module.key)}`}
				className="rounded-lg border border-slate-200 bg-white p-4 hover:border-blue-300 hover:shadow-sm transition-all group">
				<div className="flex items-center justify-between"><span className="rounded-lg bg-blue-50 p-2"><ModuleIcon module={module.key} className="size-5 text-blue-600" /></span>
					<ArrowUpRight className="size-4 text-slate-400 group-hover:text-blue-600" /></div>
				<h2 className="font-semibold mt-3">{module.label}</h2><p className="text-xs text-slate-400 mt-1">{module.items.length} authorized menus</p>
			</Link>)}
		</div>
		{!modules.length && <p>No modules are permitted for your current context. Contact your ERP administrator.</p>}
		<TaskInbox />
	</section>;
}
