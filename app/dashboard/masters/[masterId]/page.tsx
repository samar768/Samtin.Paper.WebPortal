'use client';

import { useParams } from 'next/navigation';
import { ErpMasterWorkspace } from '@/components/erp-master-workspace';
import { ErpEnvironmentWorkspace } from '@/components/erp-environment-workspace';

export default function MasterPage() {
	const { masterId } = useParams<{ masterId: string }>();
	return masterId === 'environment' ? <ErpEnvironmentWorkspace /> : <ErpMasterWorkspace key={masterId} masterId={masterId} />;
}
