import Link from 'next/link';
import { Plus } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { newSalesOrderHref, newSalesOrderPath } from '@/lib/sales-workspace-route';

export function RecordCreateLink({ href, label, compact = false, onClick }: {
	href: string; label: string; compact?: boolean; onClick?: () => void;
}) {
	const router = useRouter();
	return <Link href={href} aria-label={label} title={label} onClick={onClick} onNavigate={event => {
		if (href !== newSalesOrderPath) return;
		event.preventDefault();
		router.push(newSalesOrderHref(), { scroll: false });
	}}
		className={`erp-create-link ${compact ? 'erp-create-compact' : ''}`}>
		<Plus className="size-4 shrink-0" aria-hidden="true" />
		{!compact && <span>{label}</span>}
	</Link>;
}
