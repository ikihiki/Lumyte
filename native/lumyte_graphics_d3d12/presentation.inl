struct Swapchain
{
    ComPtr<IDXGISwapChain3> native;
    bool immediate = false;
    bool tearing = false;
};
API int window_valid(HWND window) noexcept
{
    return IsWindow(window);
}
API int tearing_supported() noexcept
{
    ComPtr<IDXGIFactory5> factory;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory))))
    {
        return 0;
    }
    BOOL supported = FALSE;
    if (FAILED(factory->CheckFeatureSupport(DXGI_FEATURE_PRESENT_ALLOW_TEARING, &supported, sizeof(supported))))
    {
        return 0;
    }
    return supported;
}
API HRESULT swapchain_create(Device* device, HWND window, UINT width, UINT height, UINT format, int immediate,
                             Swapchain** output) noexcept
{
    *output = nullptr;
    auto chain = std::unique_ptr<Swapchain>(new (std::nothrow) Swapchain());
    if (!chain)
    {
        return E_OUTOFMEMORY;
    }
    chain->immediate = immediate != 0;
    chain->tearing = tearing_supported() != 0;
    ComPtr<IDXGIFactory4> factory;
    CHECK(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)));
    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = width;
    desc.Height = height;
    desc.Format = static_cast<DXGI_FORMAT>(format);
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT | DXGI_USAGE_SHADER_INPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    desc.Flags = chain->tearing ? DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING : 0;
    ComPtr<IDXGISwapChain1> first;
    CHECK(factory->CreateSwapChainForHwnd(device->queue.Get(), window, &desc, nullptr, nullptr, &first));
    CHECK(factory->MakeWindowAssociation(window, DXGI_MWA_NO_ALT_ENTER));
    CHECK(first.As(&chain->native));
    *output = chain.release();
    return S_OK;
}
API void swapchain_destroy(Swapchain* chain) noexcept
{
    delete chain;
}
API HRESULT swapchain_resize(Swapchain* chain, UINT width, UINT height, UINT format, int immediate) noexcept
{
    if (immediate && !chain->tearing)
    {
        return DXGI_ERROR_UNSUPPORTED;
    }
    CHECK(chain->native->ResizeBuffers(2, width, height, static_cast<DXGI_FORMAT>(format),
                                       chain->tearing ? DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING : 0));
    chain->immediate = immediate != 0;
    return S_OK;
}
API HRESULT swapchain_acquire(Swapchain* chain, ID3D12Resource** output) noexcept
{
    return chain->native->GetBuffer(chain->native->GetCurrentBackBufferIndex(), IID_PPV_ARGS(output));
}
API HRESULT swapchain_present(Swapchain* chain) noexcept
{
    return chain->native->Present(chain->immediate ? 0 : 1, chain->immediate ? DXGI_PRESENT_ALLOW_TEARING : 0);
}
