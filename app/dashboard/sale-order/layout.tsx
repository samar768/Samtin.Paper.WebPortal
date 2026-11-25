'use client';

import { useSession } from '@/components/poc-session';

export default function SalesLayout({ children }: { children: React.ReactNode }) {
	const session = useSession();
	return session.capabilities.view ? children : <p role="alert">Sales access is not permitted.</p>;
}
