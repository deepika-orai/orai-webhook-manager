import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { MessageDetailModal } from "../components/MessageDetailModal";
import * as api from "../lib/api";
import { MessageListItem, MessageStatusEvent } from "../types/dashboard";

vi.mock("../lib/api", () => ({
  getMessageEvents: vi.fn(),
}));

describe("MessageDetailModal Component", () => {
  const sampleMessage: MessageListItem = {
    id: "msg-12345",
    endpointId: "ep-1",
    endpointName: "Customer Line",
    wamid: "wamid.HBgL1234567890",
    phoneNumberId: "10987654321",
    displayPhoneNumber: "+15551234567",
    recipientPhone: "+15559876543",
    currentStatus: "delivered",
    statusRank: 20,
    lastStatusTimestamp: "2026-08-25T12:00:00Z",
    conversationId: "conv-1",
    conversationOriginType: "user_initiated",
    conversationExpiresAt: "2026-08-26T12:00:00Z",
    pricingModel: "CBP",
    pricingCategory: "service",
    pricingBillable: true,
    activeErrorCode: null,
    activeErrorTitle: null,
    activeErrorMessage: null,
    activeErrorDetails: null,
    lastFailureCode: null,
    lastFailureTimestamp: null,
    lastFailureReason: null,
    bizOpaqueCallbackData: null,
    broadcastId: null,
    broadcastName: null,
    templateName: null,
    createdAt: "2026-08-25T11:55:00Z",
    updatedAt: "2026-08-25T12:00:00Z",
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads and displays chronological status events successfully", async () => {
    const mockEvents: MessageStatusEvent[] = [
      {
        id: "evt-1",
        messageId: "msg-12345",
        wamid: "wamid.HBgL1234567890",
        status: "sent",
        statusTimestamp: "2026-08-25T11:55:00Z",
        errorCode: null,
        errorTitle: null,
        errorMessage: null,
        errorDetails: null,
        errorData: null,
        createdAt: "2026-08-25T11:55:00Z",
      },
      {
        id: "evt-2",
        messageId: "msg-12345",
        wamid: "wamid.HBgL1234567890",
        status: "delivered",
        statusTimestamp: "2026-08-25T11:58:00Z",
        errorCode: null,
        errorTitle: null,
        errorMessage: null,
        errorDetails: null,
        errorData: null,
        createdAt: "2026-08-25T11:58:00Z",
      },
    ];

    vi.mocked(api.getMessageEvents).mockResolvedValueOnce(mockEvents);

    render(<MessageDetailModal message={sampleMessage} onClose={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByText("Immutable Status History")).toBeInTheDocument();
      expect(screen.getByText(/Sent/i)).toBeInTheDocument();
      expect(screen.getAllByText(/Delivered/i).length).toBeGreaterThan(0);
    });

    expect(api.getMessageEvents).toHaveBeenCalledWith("msg-12345", undefined);
  });

  it("forwards customTenantHeader when in platform admin inspection mode", async () => {
    vi.mocked(api.getMessageEvents).mockResolvedValueOnce([]);

    render(
      <MessageDetailModal
        message={sampleMessage}
        onClose={vi.fn()}
        customTenantHeader="tenant-uuid-xyz"
      />
    );

    await waitFor(() => {
      expect(api.getMessageEvents).toHaveBeenCalledWith("msg-12345", "tenant-uuid-xyz");
    });
  });

  it("handles 401 unauthorized gracefully with session expired message", async () => {
    const error401 = new Error("Authentication or tenant context is required.") as Error & { status?: number };
    error401.status = 401;
    vi.mocked(api.getMessageEvents).mockRejectedValueOnce(error401);

    render(<MessageDetailModal message={sampleMessage} onClose={vi.fn()} />);

    await waitFor(() => {
      expect(
        screen.getByText("Session expired or unauthenticated. Please log in again.")
      ).toBeInTheDocument();
    });
  });

  it("handles 403 forbidden access denied gracefully", async () => {
    const error403 = new Error("Tenant does not exist or is inactive.") as Error & { status?: number };
    error403.status = 403;
    vi.mocked(api.getMessageEvents).mockRejectedValueOnce(error403);

    render(<MessageDetailModal message={sampleMessage} onClose={vi.fn()} />);

    await waitFor(() => {
      expect(
        screen.getByText("Access denied. You do not have permission to view message events for this tenant.")
      ).toBeInTheDocument();
    });
  });

  it("handles 404 not found (cross-tenant denial) gracefully", async () => {
    const error404 = new Error("Message not found.") as Error & { status?: number };
    error404.status = 404;
    vi.mocked(api.getMessageEvents).mockRejectedValueOnce(error404);

    render(<MessageDetailModal message={sampleMessage} onClose={vi.fn()} />);

    await waitFor(() => {
      expect(
        screen.getByText("Message not found or does not belong to your tenant.")
      ).toBeInTheDocument();
    });
  });

  it("allows retrying failed requests via the Retry button", async () => {
    const errorGeneric = new Error("Network timeout");
    vi.mocked(api.getMessageEvents)
      .mockRejectedValueOnce(errorGeneric)
      .mockResolvedValueOnce([
        {
          id: "evt-recovered",
          messageId: "msg-12345",
          wamid: "wamid.HBgL1234567890",
          status: "read",
          statusTimestamp: "2026-08-25T12:05:00Z",
          errorCode: null,
          errorTitle: null,
          errorMessage: null,
          errorDetails: null,
          errorData: null,
          createdAt: "2026-08-25T12:05:00Z",
        },
      ]);

    render(<MessageDetailModal message={sampleMessage} onClose={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByText("Network timeout")).toBeInTheDocument();
    });

    const retryBtn = screen.getByRole("button", { name: /retry/i });
    fireEvent.click(retryBtn);

    await waitFor(() => {
      expect(screen.getByText(/Read/i)).toBeInTheDocument();
    });

    expect(api.getMessageEvents).toHaveBeenCalledTimes(2);
  });
});
