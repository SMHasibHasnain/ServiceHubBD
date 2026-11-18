import { StrictMode, Suspense, lazy } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, Navigate, Outlet, RouterProvider } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "./api/client";
import { AuthProvider, RequireAuth } from "./auth/AuthContext";
import { BusinessLayout, CustomerLayout, PlatformLayout, PublicLayout } from "./components/Layouts";
import { EmptyState, LinkButton, ToastProvider } from "./components/ui";
import Landing from "./pages/public/Landing";
import "./index.css";

const AuthPage = lazy(() => import("./pages/public/AuthPage"));
const Search = lazy(() => import("./pages/public/Search"));
const BusinessProfile = lazy(() => import("./pages/public/BusinessProfile"));
const Booking = lazy(() => import("./pages/public/Booking"));
const MyBookings = lazy(() => import("./pages/me/MyBookings"));
const Notifications = lazy(() => import("./pages/me/Notifications"));
const Profile = lazy(() => import("./pages/me/Profile"));
const CreateBusiness = lazy(() => import("./pages/business/CreateBusiness"));
const Dashboard = lazy(() => import("./pages/business/Dashboard"));
const Bookings = lazy(() => import("./pages/business/Bookings"));
const Services = lazy(() => import("./pages/business/Services"));
const Customers = lazy(() => import("./pages/business/Customers"));
const Reviews = lazy(() => import("./pages/business/Reviews"));
const Team = lazy(() => import("./pages/business/Team"));
const Settings = lazy(() => import("./pages/business/Settings"));
const Moderation = lazy(() => import("./pages/platform/Moderation").then((m) => ({ default: m.ModerationQueue })));
const VerificationReview = lazy(() => import("./pages/platform/Moderation").then((m) => ({ default: m.VerificationReview })));
const ModerationReports = lazy(() => import("./pages/platform/Moderation").then((m) => ({ default: m.ModerationReports })));
const ModerationHistory = lazy(() => import("./pages/platform/Moderation").then((m) => ({ default: m.ModerationHistory })));
const Inspections = lazy(() => import("./pages/platform/Inspections"));
const BusinessAnalytics = lazy(() => import("./pages/business/Analytics"));
const ModerationAnalytics = lazy(() => import("./pages/platform/PlatformAnalytics").then((m) => ({ default: m.ModerationAnalyticsPage })));
const InspectionAnalytics = lazy(() => import("./pages/platform/PlatformAnalytics").then((m) => ({ default: m.InspectionAnalyticsPage })));
const AdminAnalytics = lazy(() => import("./pages/platform/PlatformAnalytics").then((m) => ({ default: m.AdminAnalyticsPage })));
const AdminUsers = lazy(() => import("./pages/platform/Admin").then((m) => ({ default: m.AdminUsers })));
const AdminRoles = lazy(() => import("./pages/platform/Admin").then((m) => ({ default: m.AdminRoles })));
const AdminAudit = lazy(() => import("./pages/platform/Admin").then((m) => ({ default: m.AdminAudit })));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Client errors (403/404/validation) won't fix themselves on retry.
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
    },
  },
});

const Loading = () => <div className="p-10 text-center text-muted" role="status">তথ্য লোড হচ্ছে...</div>;
const Root = () => <Suspense fallback={<Loading />}><Outlet /></Suspense>;
const Protected = ({ children }: { children: React.ReactNode }) => <RequireAuth>{children}</RequireAuth>;
const NotFound = () => (
  <div className="mx-auto max-w-xl px-4 py-20">
    <EmptyState title="পৃষ্ঠাটি পাওয়া যায়নি" description="লিংকটি ভুল অথবা পৃষ্ঠাটি সরিয়ে ফেলা হয়েছে।" action={<LinkButton to="/">হোমে ফিরে যান</LinkButton>} />
  </div>
);

const router = createBrowserRouter([
  {
    element: <Root />,
    children: [
      {
        element: <PublicLayout />,
        children: [
          { path: "/", element: <Landing /> },
          { path: "/search", element: <Search /> },
          { path: "/b/:slug", element: <BusinessProfile /> },
          { path: "/b/:slug/book", element: <Protected><Booking /></Protected> },
          { path: "*", element: <NotFound /> },
        ],
      },
      { path: "/login", element: <AuthPage mode="login" /> },
      { path: "/register", element: <AuthPage mode="register" /> },
      {
        path: "/me",
        element: <Protected><CustomerLayout /></Protected>,
        children: [
          { index: true, element: <Navigate to="bookings" replace /> },
          { path: "bookings", element: <MyBookings /> },
          { path: "notifications", element: <Notifications /> },
          { path: "profile", element: <Profile /> },
        ],
      },
      { path: "/business/new", element: <Protected><CreateBusiness /></Protected> },
      {
        path: "/w/:businessId",
        element: <Protected><BusinessLayout /></Protected>,
        children: [
          { index: true, element: <Dashboard /> },
          { path: "bookings", element: <Bookings /> },
          { path: "services", element: <Services /> },
          { path: "customers", element: <Customers /> },
          { path: "reviews", element: <Reviews /> },
          { path: "team", element: <Team /> },
          { path: "settings", element: <Settings /> },
          { path: "analytics", element: <BusinessAnalytics /> },
          { path: "notifications", element: <Notifications workspace="business" /> },
        ],
      },
      {
        path: "/moderation",
        element: <Protected><PlatformLayout type="moderation" /></Protected>,
        children: [
          { index: true, element: <Moderation /> },
          { path: "v/:id", element: <VerificationReview /> },
          { path: "reports", element: <ModerationReports /> },
          { path: "history", element: <ModerationHistory /> },
          { path: "analytics", element: <ModerationAnalytics /> },
        ],
      },
      {
        path: "/inspections",
        element: <Protected><PlatformLayout type="inspection" /></Protected>,
        children: [{ index: true, element: <Inspections /> }, { path: "analytics", element: <InspectionAnalytics /> }],
      },
      {
        path: "/admin",
        element: <Protected><PlatformLayout type="administration" /></Protected>,
        children: [
          { index: true, element: <AdminAnalytics /> },
          { path: "users", element: <AdminUsers /> },
          { path: "roles", element: <AdminRoles /> },
          { path: "audit", element: <AdminAudit /> },
        ],
      },
    ],
  },
]);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <ToastProvider>
          <RouterProvider router={router} />
        </ToastProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
