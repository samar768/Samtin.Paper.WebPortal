'use client';

import Image from 'next/image';
import Link from 'next/link';
import { Suspense, useState } from 'react';
import { usePathname, useSearchParams } from 'next/navigation';
import { ChevronDown, ChevronRight, FileText, FolderClosed, House, LogOut, Menu, PanelLeftClose, UserRound, X } from 'lucide-react';
import { SessionGate, useSession } from '@/components/poc-session';
import { ModuleIcon, ModuleMark, NavigationProvider, useNavigation } from '@/components/erp-navigation';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { api, emptySchema, errorMessage } from '@/lib/poc-api';
import { filterMenu, menuTree, type MenuNode } from '@/lib/navigation';
import { RecordCreateLink } from '@/components/record-create-link';
import { menuCreateHref } from '@/lib/navigation';
import { CompanyInformation } from '@/components/company-information';
import { TaskNotifications, TaskProvider } from '@/components/erp-tasks';
import SalesWorkspaceTabs from '@/features/sales-order/SalesWorkspaceTabs';
import { salesOrderView } from '@/lib/sales-workspace-route';

function MenuBranch({ node, active, query, depth = 0, close }: {
	node: MenuNode; active: (node: MenuNode) => boolean; query: string; depth?: number; close: () => void;
}) {
	const session = useSession();
	const containsActive = (item: MenuNode): boolean => active(item) || item.children.some(containsActive);
	const [expanded, setExpanded] = useState(containsActive(node));
	const open = Boolean(query.trim()) || expanded;
	const Icon = node.children.length ? FolderClosed : FileText;
	const createHref = menuCreateHref(node, session.capabilities.add);
	return <li>
		{node.children.length ? <button className={`erp-menu-link ${containsActive(node) ? 'erp-menu-group-active' : ''}`} style={{ paddingLeft: 12 + depth * 12 }}
			aria-expanded={open} onClick={() => setExpanded(!expanded)}>
			<Icon className="size-4 shrink-0" /><span className="flex-1 text-left truncate">{node.label}</span>
			{open ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
		</button> : <div className={`erp-menu-row ${active(node) ? 'erp-menu-active' : ''}`}>
			<Link className="erp-menu-link flex-1 min-w-0"
			style={{ paddingLeft: 12 + depth * 12 }} href={node.href} aria-current={active(node) ? 'page' : undefined}
			title={node.implemented ? node.label : `${node.label} — not yet available in web POC`} onClick={close}>
			<Icon className="size-4 shrink-0" /><span className="truncate" title={node.label}>{node.label}</span>
			{!node.implemented && <span className="ml-auto text-[9px] text-slate-400 shrink-0">Soon</span>}
		</Link>
			{createHref && <RecordCreateLink href={createHref} label={`New ${node.label}`} compact onClick={close} />}
		</div>}
		{node.children.length > 0 && open && <ul>{node.children.map((child) =>
			<MenuBranch key={child.key} node={child} depth={depth + 1} active={active} query={query} close={close} />)}</ul>}
	</li>;
}

function DashboardShell({ children }: { children: React.ReactNode }) {
	const session = useSession();
	const { modules, current, warnings } = useNavigation();
	const pathname = usePathname();
	const searchParams = useSearchParams();
	const [error, setError] = useState('');
	const [busy, setBusy] = useState(false);
	const [launcher, setLauncher] = useState(false);
	const [profile, setProfile] = useState(false);
	const [moduleSearch, setModuleSearch] = useState('');
	const [menuSearch, setMenuSearch] = useState('');
	const [collapsed, setCollapsed] = useState(false);
	const [mobileOpen, setMobileOpen] = useState(false);
	const [workspaceHeader, setWorkspaceHeader] = useState<HTMLDivElement | null>(null);
	async function logout() {
		if (!window.dispatchEvent(new Event('poc:before-navigation', { cancelable: true }))) return;
		setBusy(true); setError('');
		try { await api('/session', emptySchema, { method: 'DELETE' }); window.location.replace('/'); }
		catch (failure) { setError(errorMessage(failure)); }
		finally { setBusy(false); }
	}
	const active = (node: MenuNode) => node.implemented
		? pathname === node.href || pathname.startsWith(`${node.href}/`) : searchParams.get('menu') === node.key;
	const roots = current ? filterMenu(menuTree(current.items), menuSearch) : [];
	const createItem = current?.items.find((item) => menuCreateHref(item, session.capabilities.add));
	const createHref = createItem ? menuCreateHref(createItem, session.capabilities.add) : null;
	const salesView = salesOrderView(pathname, searchParams);
	const hasWorkspaceTabs = session.capabilities.view && salesView.kind !== 'outside';
	return <div className="erp-workspace h-dvh flex flex-col overflow-hidden bg-slate-50 text-slate-700">
		<header className="grid shrink-0 grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 border-b border-slate-200 bg-white px-3 md:px-5">
			<div className="col-start-1 row-start-1 flex h-[55px] min-w-0 items-center gap-2 sm:gap-3">
				<Button variant="ghost" size="icon" aria-label="Choose module" title="All modules" onClick={() => setLauncher(true)}>
					<svg viewBox="0 0 24 24" className="size-5" aria-hidden="true">
						{[5, 12, 19].flatMap((y) => [5, 12, 19].map((x) => <circle key={`${x}-${y}`} cx={x} cy={y} r="1.6" fill="currentColor" />))}
					</svg>
				</Button>
				<Link href="/dashboard" className="flex min-w-0 items-center gap-2 font-semibold text-base text-slate-900"><ModuleMark /><span className="truncate">PaperSoft</span></Link>
				<span className="hidden sm:block h-5 border-l border-slate-200" />
				<span className="hidden sm:block truncate font-medium">{current?.label ?? 'Workspace'}</span>
			</div>
			<div className="col-start-2 row-start-1 flex min-w-0 items-center gap-2 sm:gap-3">
				<CompanyInformation />
				<TaskNotifications />
				<DropdownMenu><DropdownMenuTrigger asChild>
					<button className="flex items-center gap-2 rounded-full p-1 hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-blue-500" aria-label="Open profile menu">
						<Image src="/profile-sample.svg" alt="Sample profile avatar" width={32} height={32} className="rounded-full border border-slate-200" />
						<span className="hidden sm:block text-xs font-medium">{session.username}</span><ChevronDown className="size-3" />
					</button>
				</DropdownMenuTrigger><DropdownMenuContent align="end" className="w-56">
					<DropdownMenuLabel>Signed in as {session.username}</DropdownMenuLabel><DropdownMenuSeparator />
					<DropdownMenuItem onSelect={() => setProfile(true)}><UserRound />Profile</DropdownMenuItem>
					<DropdownMenuItem asChild><Link href="/select-company" onNavigate={event => {
						if (!window.dispatchEvent(new Event('poc:before-navigation', { cancelable: true }))) event.preventDefault();
					}}>Change company / year / site</Link></DropdownMenuItem>
					<DropdownMenuItem disabled={busy} onSelect={logout}><LogOut />Sign out</DropdownMenuItem>
				</DropdownMenuContent></DropdownMenu>
			</div>
		</header>
		<div className="flex flex-1 min-h-0">
			<nav aria-label="ERP modules" className="erp-module-rail">
				<Link href="/dashboard" className="erp-module-link" title="Dashboard"
					aria-current={pathname === '/dashboard' ? 'page' : undefined} onClick={() => setMenuSearch('')}>
					<House className="size-5" aria-hidden="true" /><span>Home</span>
				</Link>
				{modules.map((module) => <Link key={module.key}
					href={`/dashboard/modules/${encodeURIComponent(module.key)}`}
					className={`erp-module-link ${current?.key === module.key ? 'erp-module-selected' : ''}`}
					aria-label={`${module.label} module`} aria-current={current?.key === module.key ? 'true' : undefined}
					title={module.label} onClick={() => { setMenuSearch(''); setMobileOpen(false); }}>
					<ModuleIcon module={module.key} className="size-5 shrink-0" /><span>{module.label}</span>
				</Link>)}
			</nav>
			{mobileOpen && <button className="fixed inset-0 z-30 bg-slate-950/30 md:hidden" aria-label="Close module navigation" onClick={() => setMobileOpen(false)} />}
			<aside id="module-navigation" className={`erp-sidebar ${collapsed ? 'erp-sidebar-collapsed' : ''} ${mobileOpen ? 'erp-sidebar-mobile-open' : ''}`}>
				<div className="flex items-center justify-between h-12 border-b border-slate-200 px-3 shrink-0">
					{!collapsed && <span className="font-semibold truncate flex-1">{current?.label ?? 'Modules'}</span>}
					{!collapsed && createHref && createItem && <RecordCreateLink href={createHref}
						label={`New ${createItem.label}`} compact onClick={() => setMobileOpen(false)} />}
					<Button variant="ghost" size="icon" className="hidden md:inline-flex size-7" aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
						aria-expanded={!collapsed} onClick={() => setCollapsed(!collapsed)}><PanelLeftClose className="size-4" /></Button>
					<Button variant="ghost" size="icon" className="md:hidden size-7" aria-label="Close navigation" onClick={() => setMobileOpen(false)}><X className="size-4" /></Button>
				</div>
				{!collapsed && <div className="p-3 pb-1"><Input className="h-8 text-xs" aria-label="Find a menu"
					placeholder="Find a menu..." value={menuSearch} onChange={(event) => setMenuSearch(event.target.value)} /></div>}
				<nav aria-label={`${current?.label ?? 'Module'} navigation`} className="flex-1 min-h-0 overflow-y-auto py-2">
					<Link href="/dashboard" className="erp-menu-link" title="Dashboard" onClick={() => setMobileOpen(false)}>
						<House className="size-4 shrink-0" />{!collapsed && 'Dashboard'}</Link>
					{collapsed ? roots.map((node) => <button key={node.key} className="erp-menu-link w-full justify-center"
						title={node.label} aria-label={`Expand ${node.label} menu`} onClick={() => setCollapsed(false)}><FolderClosed className="size-4" /></button>)
						: <ul key={current?.key}>{roots.map((node) => <MenuBranch key={node.key} node={node}
							active={active} query={menuSearch} close={() => setMobileOpen(false)} />)}</ul>}
					{!roots.length && !collapsed && <p className="p-3 text-xs text-slate-500">No matching authorized menus.</p>}
				</nav>
				{!collapsed && <div className="border-t border-slate-200 p-3 text-[11px] text-slate-400">Web POC · menus follow ERP grants</div>}
			</aside>
			<div className="flex min-w-0 flex-1 flex-col">
				<header hidden={!hasWorkspaceTabs} className="shrink-0 border-b border-slate-200 bg-white px-3 md:px-5">
					<div ref={setWorkspaceHeader} />
				</header>
				<main className="min-h-0 flex-1 overflow-y-auto">
					<div className="md:hidden px-3 py-2 border-b bg-white flex items-center gap-2">
						<Button variant="ghost" size="icon" className="size-7" aria-label="Open navigation" aria-expanded={mobileOpen}
							onClick={() => { setCollapsed(false); setMobileOpen(true); }}><Menu className="size-4" /></Button>{current?.label ?? 'Workspace'}
					</div>
					<div className="p-3 md:p-5">
						{warnings.map((warning) => <p role="status" key={warning} className="rounded border border-amber-100 bg-amber-50 px-3 py-2 text-[11px] text-amber-800 mb-3">{warning}</p>)}
						{error && <p role="alert" className="text-red-700 mb-3">{error}</p>}{children}
						{session.capabilities.view && <SalesWorkspaceTabs key={session.contextVersion} view={salesView} headerTarget={workspaceHeader} />}
					</div>
				</main>
			</div>
		</div>
		<Dialog open={launcher} onOpenChange={setLauncher}><DialogContent className="sm:max-w-2xl">
			<DialogHeader><DialogTitle>Your modules</DialogTitle><DialogDescription>Current ERP permissions determine the modules and menus shown here.</DialogDescription></DialogHeader>
			<Input aria-label="Find a module" placeholder="Find a module..." value={moduleSearch} onChange={(event) => setModuleSearch(event.target.value)} />
			<div className="grid grid-cols-2 sm:grid-cols-3 gap-2 max-h-[60vh] overflow-y-auto">
				{modules.filter((module) => module.label.toLowerCase().includes(moduleSearch.toLowerCase())).map((module) =>
					<Link key={module.key} href={`/dashboard/modules/${encodeURIComponent(module.key)}`}
						aria-current={current?.key === module.key ? 'true' : undefined} onClick={() => { setLauncher(false); setMenuSearch(''); }}
						className={`rounded-lg border p-4 hover:border-blue-300 hover:bg-blue-50 flex flex-col items-center gap-2 text-sm ${current?.key === module.key ? 'border-blue-400 bg-blue-50' : 'border-slate-200'}`}>
						<ModuleIcon module={module.key} className="size-6 text-blue-600" /><span>{module.label}</span>
						<span className="text-[11px] text-slate-400">{module.items.length} menus</span>
					</Link>)}
			</div>
			{!modules.length && <p>No modules are permitted in this context.</p>}
		</DialogContent></Dialog>
		<Dialog open={profile} onOpenChange={setProfile}><DialogContent>
			<DialogHeader><DialogTitle>Your profile</DialogTitle><DialogDescription>Identity and context supplied by the ERP. The avatar is a sample image.</DialogDescription></DialogHeader>
			<div className="flex gap-4 items-center"><Image src="/profile-sample.svg" alt="Sample avatar" width={64} height={64} className="rounded-full" />
				<div><p className="font-semibold">{session.username}</p><p className="text-sm text-slate-500">{session.companyName}</p></div></div>
			<dl className="grid grid-cols-2 gap-3 text-sm"><dt>Company</dt><dd>{session.company}</dd><dt>Financial year</dt><dd>{session.financialYear}</dd><dt>Site</dt><dd>{session.siteName} ({session.site})</dd></dl>
		</DialogContent></Dialog>
	</div>;
}

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
	return <SessionGate><Suspense fallback={<p className="p-6">Loading workspace...</p>}>
		<NavigationProvider><TaskProvider><DashboardShell>{children}</DashboardShell></TaskProvider></NavigationProvider>
	</Suspense></SessionGate>;
}
