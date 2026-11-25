import type { Metadata } from 'next';
import { Toaster } from '@/components/ui/sonner';
import './globals.css';

export const metadata: Metadata = {
	title: 'PaperSoft',
	description: 'Samar ERP Management System',
};

export default function RootLayout({
	children,
}: Readonly<{
	children: React.ReactNode;
}>) {
	return (
		<html lang="en">
			<body className="font-sans antialiased">
				{children}
				<Toaster position="top-right" richColors closeButton duration={4500} visibleToasts={3}
					offset={{ top: 64, right: 20 }} mobileOffset={{ top: 64, left: 12, right: 12 }}
					containerAriaLabel="App messages" />
			</body>
		</html>
	);
}
