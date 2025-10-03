# Virtual Shadow MapsMatej Sakmary, Jake Ryan， Justin Hall,and Alessio Lustri

# 12.1 Introduction

Shadow mapping is ubiquitous in real-time applications due to its good performance and compatibility with existing rasterization pipelines. However, the growing demands for graphical fidelity and performance continue to increase the complexity of shadow mapping techniques. The simplest way to increase the quality of a shadow map—increasing its resolution—scales its memory cost quadratically and increases the likelihood of cache misses when sampling,leading to worse performance. This makes it an impractical way to achieve acceptable quality in diverse scenes. Moreover,shadow maps suffer from two major forms of aliasing: perspective aliasing and projective aliasing, shown in Figure 12.1.

Perspective aliasing occurs when screen pixels do not map to shadow texels in a one-to-one manner. In a basic shadow map implementation, this can be observed by simply moving the viewer closer to or farther from the edge of a shadow. When close, the edge has a jagged appearance due to individual shadow texels mapping to many screen pixels.Even when filtered,a slight shift in occluding geometry or the casting light will appear jittery. When far from the shadow, the shadow map over-samples the scene and consumes more memory than necessary.

![](images/05bbd6dc3cbd567ef7e3918ade4a89f7793d76074435f016973ab60862f4b7ad.jpg)  
Figure 12.1. Left:Shadows exhibiting perspective aliasing from insufficient shadow map resolution. Right:Projective aliasing can be seen by the jagged shadow edges on the wall.

When the direction of incident light is almost parallel to the surface,projective aliasing occurs. In this case, the projection of a texel onto that surface will cover a large area,making the depth sample an inaccurate representation of the blocker. For an observer, this gives the edges of shadows a jagged appearance, even when there is otherwise no visible perspective aliasing.

# 12.1.1 Previous Work

Cascaded Shadow Maps (CSMs) is a popular algorithm used to alleviate both types of aliasing in directional lights [Dimitrov O7]. In CSMs, the viewer's frustum is partitioned into smaller frusta,each fit with its own light space matrix and assigned a shadow map. The scene is then rendered to each shadow map using the cascade's frustum. The result is an improved distribution of shadow texels.A denser shadow map is placed near the viewer where detail is most needed, while distant regions receive coarser shadows. Sample Distribution Shadow Maps (SDSMs) improve CSMs by using a per-frame analysis of the rendered scene to calculate tight-fitting frusta,maximizing the efficiency of the shadow sample distribution regardless of scene content [Lauritzen et al.11].

Although these techniques reduce aliasing,the fundamental compromise between shadow quality and memory cost remains. It is thus common for rendering engines to provide several options for shadows,each with their own set of tradeoffs. For example,screen-space ray tracing is often used for sharper and more detailed contact shadows in contrast to those provided by shadow mapping implementations.

With the release of Unreal Engine 5 (UE5)，Epic Games added Virtual Shadow Maps that scale to support the detailed geometry enabled by Nanite [Epic Games 22, Karis 21].

# 12.1.2 Virtual Memory Overview

Virtual Shadow Maps (VSMs),as the name might suggest, are an application of virtual textures [van Waveren O9,Barrett O8] for shadow mapping. Virtual texturing itself is essentially a generalization of virtual memory.

By separating the logical address space (seen by the user） from its physical backing,the appearance of a very large contiguous memory space can be achieved without the need to reserve physical memory for it. This is done by dividing the logical address space into pages that are dynamically backed by physical memory.

Virtual memory provides numerous benefits in operating systems. For virtual texturing and thus also VSMs,its primary purpose is to provide an abstraction allowing for a highly reduced memory footprint. That is,only the pages of a VSM that arevisible need to be allocated.

As mentioned above, it is common to combine multiple shadowing algorithms to achieve the best visual result.The efficient distribution of shadow texels that VSMs offer makes them viable as a standalone technique. Thus,VSMs also serve to unify multiple shadowing paths,making it easier to author scenes without having to consider how each path may be affected.

Inspired by UE5,we re-derive and implement Virtual Shadow Maps in our renderer. This article and the accompanying demo application serve as pedagogical tools to understand this technique. This is mainly motivated by the lack of resources that describe this technique in detail at the time of writing. We could find only one resource [Haar 15] that mentioned VSMs,and it only does so in passing.

# 12.2 Implementation

Our implementation supports a VSM for a directional light，consisting of 16 overlapping $4 0 9 6 \times 4 0 9 6$ cascades arranged in a concentric manner. Every cascade consists of a sparsely allocated set of pages,with each page spanning $1 2 8 \times 1 2 8$ texels. Each cascade's frustum is twice the size of the previous cascade's. The size of the first cascade,specified in world-space units, thus determines the resolution and quality of the entire VSM.In our implementation,we chose to make our first cascade $2 \times 2$ meters wide,with the last cascade covering a diameter of 65 km. The virtual resolution of cascades, their number,and the resolution of pages can vary.

A virtual page table (VPT） is used to map virtual addresses to physical addresses when we sample the shadow map. The VPT also contains metadata: page allocation status, visibility status,and a dirty flag. The allocation status flag marks pages that are backed by a physical page. The visibility status fag indicates whether the page is visible to the viewer in this frame. The dirty status flag indicates that the physical page contents are no longer valid and should be redrawn. We further define a physical page table (PPT) that provides the mapping of physical pages to virtual pages (the inverse of the VPT).

Sparse (or tiled) resources in graphics APIs initially seem like an obvious choice for VSMs. However, the API for sparse resources does not allow for an indirect GPU-driven approach. To know which VSM pages need to be backed by physical memory would require GPU readback. This adds unacceptable latency and leads to shadow pop-in. Our implementation opts for a manual allocation scheme that allows for execution of every part of the algorithm on the GPU and avoids this round trip latency.

Page Caching Caching is a common and important optimization for shadow mapping in the semi-static scenes frequently found in real-time applications. We implement a per-page caching scheme for VSMs. After a page is drawn and sampled,it is not discarded. Instead,its contents are preserved,and the allocation is left untouched,continuing its lifetime into the next frame.By doing this, we minimize the number of required draws and allow for more efficient culling.

To support caching,individual cascade frustums are snapped to the page grid.Further,the light position is constrained so that,when modified, it slides along a plane parallel to the near-plane of the respective light matrix. This constraint is necessary for the depth stored in cached pages to remain valid even after translating the light matrix.

As the cascade frustum moves to follow the main camera, new pages,previously located on the edge just outside of the cascade frustum,might need to be drawn. In order to preserve previously cached pages,we utilize a sliding window,also called 2D wraparound addressing in [Asirvatham and Hoppe 05]. As new pages enter the frustum when it moves,old pages exit,as illustrated by Figure 12.2. With a sliding window, the newly entered pages are mapped to the location in the VPT previously occupied by old,exiting pages. This requires us

![](images/9090d42e5b5f0fe626bda4db3678260c638726e94ae8586b001674b8ef839e14.jpg)  
Figure 12.2. Sliding window and caching

ivec3 virtual_page_coords_to_wrapped_coords(ivec3 page_coords， ivec2 cascade_offset)   
{ // Make sure that the virtual page coordinates are in page // table bounds to prevent erroneous wrapping if( any(lessThan(page_coords.xy， ivec2(o))) Il any(greaterThan(page_coords.xy， ivec2(VSM_PAGE_TABLE_RESOLUTION -1))) { return ivec3(-1， -1， page_coords.z); } const ivec2 offset_page_coords $\mathbf { \tau } = \mathbf { \tau }$ page_coords.xy + cascade_offset.xy; const ivec2 wrapped_page_coords $\mathbf { \tau } = \mathbf { \tau }$ ivec2(mod(offset_page_coords.xy, Vec2（VSM_PAGE_TABLE_RESOLUTION))）; // Third coordinate specifies the VSM cascade level return ivec3(wrapped_page_coords， page_coords.z);   
}

to store a per-cascade offset of the respective light matrix position from the origin. Therefore,a translation function projecting from virtual page coordinates into wrapped coordinates is given by Listing 12.1. The wrapped coordinates are then used to look up VPT entries.

In dynamic scenes,it is sometimes necessary to invalidate cached pages.For example, changing the direction of a directional light will invalidate all its pages. A mechanism to invalidate cached pages overlapped by moving objects is also necessary.

We wil now explain in further detail each step of our approach to implementing Virtual Shadow Maps. Figure 12.3 gives an overview of our implementation of VSMs.

# 12.2.1 Bookkeeping

The bookkeeping phase comprises a set of smaller tasks that prepare the data to draw the shadow map.This consists of five steps,which will be described in detail in this section.The execution flow diagram,along with all the resources used,can be seen in Figure 12.4.

Freeing Invalidated Pages The first step in bookkeeping is to free cached pages that have been invalidated by a change in the position of the light matrix or by the page intersecting a moving object. From the CPU，we receive a percascade input containing the number of pages the light matrix moved compared to the previous frame and a bitmask representing the set of pages invalidated by dynamic objects.We then simply free the appropriate entries in the VPT by resetting their allocation status to O. The code to free invalidated pages is then given by Listing 12.2.

![](images/62a0df25fe7266406e9fd9f0f128487dd9d07dd1f5c365326c9089d3a3d3bbb8.jpg)  
Figure 12.3.Virtual memory system overview.

![](images/3415620c74c304b2c909ca759f0b11a87f0367ff412088771586382f43c18393.jpg)  
Figure 12.4. Setup flowchart.   
Listing 12.2.Function used to free invalidated pages.First,we determine if the virtual page needs to be freed from the data sent from the CPU. If the page does need freeing, we translate into wrapped corrdinates and reset its state.

// Cascade shift gives the offset in pages relative to the cascade   
// position previous frame cascade offset is the offset used   
// for the sliding window   
void free_wrapped_pages(ivec2 cascade_shift， ivec2 cascade_offset)   
{ const ivec3 page_coords $\mathbf { \sigma } = \mathbf { \sigma }$ ivec3(gl_LocalInvocationID.xyz); const bool should_clear_wrap = (clear_offset.x > O && page_coords.x < cascade_shift.x) Il (clear_offset.x < O && page_coords.x > VSM_PAGE_TABLE_RESOLUTION $^ +$ (cascade_shift.x - 1)) Il (clear_offset.y > O && page_coords.y < cascade_shift.y) ll (clear_offset.y < O && page_coords.y > VSM_PAGE_TABLE_RESOLUTION +（cascade_shift.y - 1)）; const bool should_clear_dynamic $\mathbf { \lambda } = \mathbf { \lambda }$ extract_page_bit_from_mask( page_coords，bitmask); const ivec3 wrapped_page_coords $\mathbf { \tau } = \mathbf { \tau }$ virtual_page_coords_to_wrapped_coords(page_coords， cascade_offset); if(should_clear_wrap Il should_clear_dynamic) { const uint page_entry $\mathbf { \tau } = \mathbf { \tau }$ imageLoad(virtual_page_table, wrapped_page_coords).r; if(get_is_allocated(page_entry)) { imageStore(virtual_page_table， wrapped_page_coords，uvec4(0) ）； } }   
}

Mark Visible Pages Each screen pixel is assigned a shadow cascade index based on a heuristic.A heuristic is used because simply choosing the most detailed possible cascade does not provide adequate control over shadow detail and memory consumption.

We describe two different heuristics,each of which has its own advantages and disadvantages.

The first heuristic prioritizes achieving pixel-perfect shadows—a one-to-one mapping of screen pixels to shadow map texels. This heuristic is similar to how mipmap levels are selected when sampling textures. To achieve this,we obtain the world-space size of each texel by unprojecting its opposing sides into world space and finding their distance.The clip level is then decided by the following formula:

$$
\mathrm { l e v e l } = \operatorname* { m a x } \left( \left\lceil \log _ { 2 } \left( \frac { T _ { w } } { T _ { c _ { 0 } } } \right) \right\rceil , 0 \right) ,
$$

where $T _ { w }$ and $T _ { c _ { 0 } }$ are the screen-space texel world size and cascade O texel world size,respectively. Although this heuristic has a theoretically optimal cascade selection,it does not take into account the available resolution of each cascade. This may cause a pixel to fall into a cascade outside of the cascade frustum itself. This happens when the resolution of the shadow map is close to or higher than the resolution of the screen. In practice,a biasing term can be introduced, offsetting the cascade selection to favor higher cascades.

The second heuristic is simpler and does not achieve as tight of a screen pixel to shadow texel mapping as the first. In contrast to the first heuristic,the issue of mapping texels outside of each cascade frustum is not present. The formula of the second heuristic is given by the following equation:

$$
\mathrm { l e v e l } = \operatorname* { m a x } \left( \Bigl \lceil \log _ { 2 } \left( \frac { d } { s _ { c _ { 0 } } } \right) \Bigr \rceil , 0 \right) ,
$$

where $d$ is the world-space distance of the texel from the camera and ${ \mathit { s } } _ { { \mathit { c } } _ { 0 } }$ is the side length of the Oth cascade's frustum. An advantage of this heuristic is that the chosen level for a given location in world space is rotationally invariant; that is,if the viewer rotates their camera,no virtual page that remains in view will be invalidated.

To know which pages need to be drawn,we first need to find the set of pages that are visible in this frame. This consists of a pass over the current frame depth buffer. Similarly to SDSM, VSM requires a full-resolution depth buffer drawn from the viewer's perspective.For each depth texel, one of the previously described heuristics is used to select the corresponding cascade. Using the selected cascade matrices,the fragment is reprojected into the cascade UV coordinates and the corresponding entry from the VPT is read. Then one of three actions is taken, depending on the state of the page stored in the page table.If the page has not yet been allocated,its coordinates are added to the buffer storing allocation requests for this frame. If the page is already allocated but not yet marked as visible,we mark it as visible this frame.Lastly, if the page is allocated and marked as visible,we do nothing (Figure 12.5).

![](images/947d081238328450a885106718338f032ae1e98df71431a63221e6299260086c.jpg)  
Figure 12.5.Overview of the depth analysis used to select a suitable cascade for each pixel.

Figure 12.6 shows how we visualized cascade selection and page states for debugging.

![](images/b3d5cfe2a0ac5da6804f0837593713ff2e5393c033092404bfc1b1058561cf01.jpg)  
Figure 12.6.Left:Visualization showing the cascade levels and visible pages. The pink pages are those invalidated by the motion of a moving sphere whose shadow is visible. Right:The status of each page in first VSM cascade is shown. Currently visible pages are denoted by bright red.Pages that are cached,but not visible are denoted by dark red. Black pages consume no physical memory.

Filling Allocator Buffers In preparation for allocation,we distribute the physical pages into two buffers. The first bufer contains the coordinates of all pages that have not yet been allocated to a VSM page. The second buffer holds the coordinates of all physical pages that have been allocated but are backing virtual pages that were not marked as visible in this frame. If a physical page is both allocated and backs a shadow map page that is visible in this frame,it is not inserted into either buffer since it will not be freed.

Allocating Pages The allocation pass maps the shadow map pages stored in the allocation requests buffer to physical pages stored in the two buffers prepared in the last step. The allocator attempts to use physical pages from the first buffer, holding unallocated pages. Only when there are insufficient free pages are the pages in the second buffer used (Figure 12.7). In this case, the page must first be freed before it is reused. Since the physical page entry stores the coordinates of the virtual page that it backs,freeing the physical page and assigning it to a virtual page can both be done in the same pass. Before proceeding with the allocation，we look up the virtual page entry currently backed by the physical page and reset it to the not allocated state. To complete the allocation，both the physical page and new virtual page are marked as allocated,and each stores the others’ coordinates for future reference.

Clear Pages Before drawing the VSM, we need to clear the physical memory of the dirty pages.We cannot simply clear all physical pages,as we wish to preserve cached pages. Therefore, we use the allocation requests buffer to determine pages that have just been allocated and need to be cleared. Then,an indirect dispatch clears the corresponding physical pages.

![](images/b4ea34f8e1fd1c069320102803eff5e29508b9f8155a240c8c6c34bfad208c13.jpg)  
Figure 12.7.Overview of the page allocation system.

# 12.2.2 Drawing Phase

Hierarchical Page Culling After determining which virtual pages are visible to the viewer and allocating physical pages to back them,the scene is rendered to each cascade. Due to the VSM having many cascades,it is critical to have effective and granular scene culling. In a typical frame,only a fraction of the pages in each cascade willbe marked as dirty at any time. With this knowledge,we define a structure called the hierarchical page buffer (HPB) (Figure 12.8). Similarly to how a hierarchical Z-buffer (Hi-Z) [Greene et al. 93] is built from the depth buffer for the purpose of occlusion culling,we build the HPB from each cascade VPT for the purpose of culing geometry that does not overlap any dirty page. Thus,each HPB level is constructed as a $2 \times 2$ reduction of the lower level, with the lowest level reducing the VPT itself. Contrary to Hi-Z, we do not reduce the depth. Instead,we do a max reduction of the dirty status flag marking the page as being drawn into this frame.Figure 12.9 illustrates this culling scheme.

To achieve granular culling,our drawing was implemented with meshlets combined with mesh shaders.One meshlet is mapped to a single task shader invocation. The task (or amplification) shader performs frustum culling followed by culling against the HPB.

![](images/555ad2aa879e06d74805d399ec7cd5fc391d2d1f8a272a15f643ef24f93cb316.jpg)  
Figure 12.8.Mipmap chain for the hierarchical page buffer.Red: dirty page.Black: cached or unused page.

When culling geometry against the HPB,we follow a process almost identical to culling against a Hi-Z.First, the meshlet bounding box is projected by the cascade light matrix. The minimum and maximum bounds of the projected bounding box are then calculated and the appropriate level of the HPB is selected. The selection is done in the same way as when using Hi-Z:we select the level in which the bounding-box bounds intersect exactly four texels.Lastly, if any of the four intersected texels is marked as dirty, the meshlet survives culling; otherwise,it isdiscarded.

After culling,a group of 32 mesh shader threads is dispatched for each of the surviving meshlets. The fragment shader calculates the page entry coordinates, translates them into wrapped coordinates,and looks up the corresponding VPT entry. Fragments that are mapped to page entries that are not marked both allocated and dirty do nothing. Fragments that map onto a valid VPT page read the physical page coordinates from the virtual page entry. Following this, the fragment offset inside the physical page is calculated,and an atomic min operation is used to store the new depth. The fragment shader code is presented in Listing 12.3.

![](images/4b78279a1e2129921bb5a4c19a6aa65b75151b8badd6d0562a6a3e21ab031e6a.jpg)  
Figure 12.9.HPB culling scheme overview.

While this culling system is used for our implementation of VSMs,it can be applied more generally to any shadow mapping algorithm.To build an HPB for culling, the only input needed is the set of visible shadow pages,which is derived from the Mark Visible Pages pass.

layout (location = O) in flat uint cascade_index;   
layout (location $\mathbf { \Sigma } = \mathbf { \Sigma }$ 1) in flat ivec2 cascade_offset;   
void main()   
{ const vec2 virtual_uv $\mathbf { \tau } = \mathbf { \tau }$ gl_FragCoord.xy / VSM_TEXTURE_RESOLUTION; const ivec3 page_coords $\mathbf { \lambda } = \mathbf { \lambda }$ ivec3(floor(virtual_uv \* VSM_PAGE_TABLE_RESOLUTION），cascade_index）; const ivec3 wrapped_page_coords $\mathbf { \lambda } = \mathbf { \lambda }$ virtual_page_coords_to_wrapped_coords(page_coords， cascade_offset); const uint page_entry $\mathbf { \tau } = \mathbf { \tau }$ imageLoad(virtual_page_table, wrapped_page_coords).r; if(get_is_allocated(page_entry) && get_is_dirty(page_entry)) { const ivec2 physical_page_coords $\mathbf { \Sigma } = \mathbf { \Sigma }$ unpack_physical_page_coords( page_entry）; const ivec2 virtual_texel_coords $\mathbf { \tau } = \mathbf { \tau }$ ivec2(gl_FragCoord.xy); const ivec2 in_page_texel_coords $\mathbf { \tau } = \mathbf { \tau }$ ivec2(mod(virtual_texel_coord ，VSM_PAGE_SIZE)）; const ivec2 in_memory_offset $\mathbf { \sigma } = \mathbf { \sigma }$ physical_page_coords \* VSM_PAGE_SIZE; const ivec2 memory_texel_coords $\mathbf { \tau } = \mathbf { \tau }$ in_memory_offset + in_page_texel_coord; imageAtomicMin(vsm_memory，memory_texel_coords， floatBitsToUint( gl_FragCoord.z)）; }   
}

# 12.2.3 Sampling Phase

When sampling the VSM,the cascade of the shaded pixel needs to be determined. For this purpose,we reuse the heuristic that was used to mark the visible pages. The receiver's position is transformed into the light space of the sampled cascade. This is then translated into VPT texture coordinates and wrapped to obtain the final VPT entry location. From the VPT entry, the coordinates of the physical texel in the physical page are calculated following the same process utilized when drawing VSM pages.

Although the VSM is detailed,a single shadow sample per shaded texel is insufficient for compelling soft shadows. For this purpose,further filtering of the VSM needs to be used. Common shadow filtering algorithms,such as PCF and PCSS [Fernando O5], can be used together with VSMs.

Although this approach works for most shaded pixels,an issue arises when the sampling region crosses the physical page boundary. The pass marking the visible pages assumes that we will perform a shadow test only for the texels visible in this frame.When filtering, this assumption is broken. One or more samples from the filtering region may fall into parts of the world that are not visible from the main camera. The guarantee of at least one physical page backing each sampled texel thus no longer holds.

This can cause artifacts when the edge of a shadow is near the edge of a page.To mitigate the issue and restore the guarantee when shadow filtering is desired, the Mark Visible Pages pass must be modified. Instead of marking only the page directly corresponding to the visible texel,we mark all pages lying in a region around it. This region must be greater than or equal to the size of the filtering region.
