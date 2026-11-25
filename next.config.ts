import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
	cacheComponents: true,
	async rewrites() {
		return [{ source: '/api/poc/:path*', destination: `${process.env.POC_API_URL ?? 'http://localhost:5080'}/api/:path*` }];
	},
};

export default nextConfig;
